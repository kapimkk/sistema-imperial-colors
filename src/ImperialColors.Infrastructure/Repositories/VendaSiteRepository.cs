using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Domain.ReadModels;
using ImperialColors.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ImperialColors.Infrastructure.Repositories;

/// <summary>
/// Vendas do site, lidas do registro de idempotência da integração
/// (<c>integration.imperial_sync_operations</c>) e ligadas às tabelas do próprio sistema.
///
/// <b>Somente leitura.</b> A venda online é criada pela função <c>integration.apply_sale_create</c>
/// (chamada pelo ImperialSync); nada aqui grava no banco.
///
/// Por que SQL e não uma entidade do EF: o schema <c>integration</c> é instalado À MÃO pelos
/// scripts do ImperialSync, não por migration deste sistema. Mapear a tabela no
/// <c>AppDbContext</c> faria o próximo <c>dotnet ef migrations add</c> tentar criá-la — e
/// quebraria a instalação do cliente que já a tem. A consulta, então, é um SELECT puro e
/// parametrizado, e a tela explica "não instalada" quando o schema não existe.
///
/// A ligação com <c>vendas</c> é <c>LEFT JOIN</c> de propósito: o registro da integração não tem
/// chave estrangeira para a venda (uma FK impediria o sistema de excluir uma venda). Se a venda
/// foi apagada depois, a linha continua na lista como "Venda removida".
/// </summary>
public class VendaSiteRepository : IVendaSiteRepository
{
    private const string FusoDaLoja = "America/Sao_Paulo";

    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public VendaSiteRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<PaginaVendasSite> ObterPaginadoAsync(
        int pagina, int itensPorPagina, string? termoBusca = null, CancellationToken cancellationToken = default)
    {
        pagina = Math.Max(1, pagina);
        itensPorPagina = Math.Clamp(itensPorPagina, 1, 200);
        var deslocamento = (pagina - 1) * itensPorPagina;
        var padrao = string.IsNullOrWhiteSpace(termoBusca) ? null : MontarPadraoContem(termoBusca.Trim());

        await using var context = _contextFactory.CreateDbContext();

        try
        {
            var total = padrao is null
                ? await context.Database.SqlQuery<int>($"""
                    SELECT count(*)::int AS "Value"
                    FROM integration.imperial_sync_operations AS o
                    WHERE o.operation_type = 'SALE_CREATE'
                    """).SingleAsync(cancellationToken)
                : await context.Database.SqlQuery<int>($"""
                    SELECT count(*)::int AS "Value"
                    FROM integration.imperial_sync_operations AS o
                    LEFT JOIN public.vendas AS v ON v.id = o.local_sale_id
                    LEFT JOIN public.clientes AS c ON c.id = v.cliente_id
                    WHERE o.operation_type = 'SALE_CREATE'
                      AND (o.site_order_number ILIKE {padrao}
                        OR o.local_sale_number ILIKE {padrao}
                        OR v.nome_comprador_cupom ILIKE {padrao}
                        OR c.nome ILIKE {padrao})
                    """).SingleAsync(cancellationToken);

            if (total == 0)
                return new PaginaVendasSite { Situacao = SituacaoIntegracaoSite.Disponivel };

            var linhas = padrao is null
                ? await context.Database.SqlQuery<LinhaSql>($"""
                    SELECT o.operation_id AS "OperacaoId",
                           o.site_order_number AS "PedidoSite",
                           o.local_sale_id AS "VendaId",
                           COALESCE(v.numero_venda, o.local_sale_number) AS "NumeroVenda",
                           (v.id IS NOT NULL) AS "VendaExiste",
                           COALESCE(v.ativo, false) AS "VendaAtiva",
                           v.status AS "Status",
                           COALESCE(NULLIF(btrim(v.nome_comprador_cupom), ''), c.nome) AS "Cliente",
                           v.data_venda AS "DataVenda",
                           v.total AS "Total",
                           COALESCE(pg.forma_pagamento, v.forma_pagamento) AS "FormaPagamento",
                           COALESCE(pg.quantidade_parcelas, v.quantidade_parcelas) AS "Parcelas",
                           COALESCE(pg.quantidade_pagamentos, 0)::int AS "QuantidadePagamentos",
                           (o.processed_at AT TIME ZONE {FusoDaLoja}) AS "SincronizadoEm"
                    FROM integration.imperial_sync_operations AS o
                    LEFT JOIN public.vendas AS v ON v.id = o.local_sale_id
                    LEFT JOIN public.clientes AS c ON c.id = v.cliente_id
                    LEFT JOIN LATERAL (
                        SELECT p.forma_pagamento, p.quantidade_parcelas, count(*) OVER () AS quantidade_pagamentos
                        FROM public.venda_pagamentos AS p
                        WHERE p.venda_id = v.id AND p.ativo
                        ORDER BY p.ordem, p.id
                        LIMIT 1
                    ) AS pg ON true
                    WHERE o.operation_type = 'SALE_CREATE'
                    ORDER BY o.processed_at DESC, o.operation_id DESC
                    OFFSET {deslocamento} LIMIT {itensPorPagina}
                    """).ToListAsync(cancellationToken)
                : await context.Database.SqlQuery<LinhaSql>($"""
                    SELECT o.operation_id AS "OperacaoId",
                           o.site_order_number AS "PedidoSite",
                           o.local_sale_id AS "VendaId",
                           COALESCE(v.numero_venda, o.local_sale_number) AS "NumeroVenda",
                           (v.id IS NOT NULL) AS "VendaExiste",
                           COALESCE(v.ativo, false) AS "VendaAtiva",
                           v.status AS "Status",
                           COALESCE(NULLIF(btrim(v.nome_comprador_cupom), ''), c.nome) AS "Cliente",
                           v.data_venda AS "DataVenda",
                           v.total AS "Total",
                           COALESCE(pg.forma_pagamento, v.forma_pagamento) AS "FormaPagamento",
                           COALESCE(pg.quantidade_parcelas, v.quantidade_parcelas) AS "Parcelas",
                           COALESCE(pg.quantidade_pagamentos, 0)::int AS "QuantidadePagamentos",
                           (o.processed_at AT TIME ZONE {FusoDaLoja}) AS "SincronizadoEm"
                    FROM integration.imperial_sync_operations AS o
                    LEFT JOIN public.vendas AS v ON v.id = o.local_sale_id
                    LEFT JOIN public.clientes AS c ON c.id = v.cliente_id
                    LEFT JOIN LATERAL (
                        SELECT p.forma_pagamento, p.quantidade_parcelas, count(*) OVER () AS quantidade_pagamentos
                        FROM public.venda_pagamentos AS p
                        WHERE p.venda_id = v.id AND p.ativo
                        ORDER BY p.ordem, p.id
                        LIMIT 1
                    ) AS pg ON true
                    WHERE o.operation_type = 'SALE_CREATE'
                      AND (o.site_order_number ILIKE {padrao}
                        OR o.local_sale_number ILIKE {padrao}
                        OR v.nome_comprador_cupom ILIKE {padrao}
                        OR c.nome ILIKE {padrao})
                    ORDER BY o.processed_at DESC, o.operation_id DESC
                    OFFSET {deslocamento} LIMIT {itensPorPagina}
                    """).ToListAsync(cancellationToken);

            return new PaginaVendasSite
            {
                Situacao = SituacaoIntegracaoSite.Disponivel,
                Itens = linhas.Select(Converter).ToList(),
                Total = total
            };
        }
        catch (Exception ex) when (ClassificarFalhaDeAcesso(ex) is { } situacao)
        {
            // Schema ausente ou sem permissão não é erro de programa: é o estado de instalação
            // do banco, e a tela explica o que fazer em vez de mostrar uma pilha de erro.
            return new PaginaVendasSite { Situacao = situacao };
        }
    }

    /// <summary>
    /// 42P01/3F000 (tabela ou schema inexistente) → integração não instalada; 42501 (permissão
    /// negada) → o usuário que o sistema usa não lê o schema <c>integration</c>. Qualquer outro
    /// erro continua sendo erro de verdade e sobe.
    /// </summary>
    internal static SituacaoIntegracaoSite? ClassificarFalhaDeAcesso(Exception exception)
    {
        for (var atual = exception; atual is not null; atual = atual.InnerException)
        {
            if (atual is PostgresException pg)
            {
                return pg.SqlState switch
                {
                    PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName => SituacaoIntegracaoSite.NaoInstalada,
                    PostgresErrorCodes.InsufficientPrivilege => SituacaoIntegracaoSite.SemPermissao,
                    _ => null
                };
            }
        }

        return null;
    }

    /// <summary>Texto digitado vira "contém", sem deixar <c>%</c>, <c>_</c> e <c>\</c> funcionarem
    /// como curinga: quem procura "20261006_0001" quer exatamente esse texto.</summary>
    internal static string MontarPadraoContem(string termo)
        => "%" + termo.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private static LinhaVendaSite Converter(LinhaSql linha) => new()
    {
        OperacaoId = linha.OperacaoId,
        PedidoSite = linha.PedidoSite,
        VendaId = linha.VendaId,
        NumeroVenda = linha.NumeroVenda,
        VendaExiste = linha.VendaExiste,
        VendaAtiva = linha.VendaAtiva,
        Status = linha.Status is { } status ? (StatusVenda)status : null,
        Cliente = linha.Cliente,
        DataVenda = linha.DataVenda,
        Total = linha.Total,
        FormaPagamento = linha.FormaPagamento is { } forma ? (FormaPagamento)forma : null,
        Parcelas = linha.Parcelas,
        QuantidadePagamentos = linha.QuantidadePagamentos,
        SincronizadoEm = linha.SincronizadoEm
    };

    /// <summary>Linha crua da consulta; os nomes das propriedades são os apelidos das colunas.</summary>
    internal sealed class LinhaSql
    {
        public Guid OperacaoId { get; set; }
        public string PedidoSite { get; set; } = string.Empty;
        public int VendaId { get; set; }
        public string NumeroVenda { get; set; } = string.Empty;
        public bool VendaExiste { get; set; }
        public bool VendaAtiva { get; set; }
        public int? Status { get; set; }
        public string? Cliente { get; set; }
        public DateTime? DataVenda { get; set; }
        public decimal? Total { get; set; }
        public int? FormaPagamento { get; set; }
        public int? Parcelas { get; set; }
        public int QuantidadePagamentos { get; set; }
        public DateTime SincronizadoEm { get; set; }
    }
}
