using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Domain.ReadModels;
using ImperialColors.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ImperialColors.Infrastructure.Repositories;

public class RelatorioAnalyticsRepository : IRelatorioAnalyticsRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public RelatorioAnalyticsRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<IReadOnlyList<LinhaRelatorioVendaExternaResumo>> ObterLinhasVendasExternasPorPeriodoAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        return await context.ItensVendaExterna
            .AsNoTracking()
            .Where(i => i.VendaExterna.DataVenda >= inicio && i.VendaExterna.DataVenda <= fim)
            .OrderBy(i => i.VendaExterna.DataVenda)
            .ThenBy(i => i.VendaExterna.NumeroVendaExterna)
            .ThenBy(i => i.Id)
            .Select(i => new LinhaRelatorioVendaExternaResumo
            {
                DataVenda = i.VendaExterna.DataVenda,
                CodigoVenda = i.VendaExterna.NumeroVendaExterna,
                ProdutoItem = i.NomeProduto,
                QuantidadeVendida = i.Quantidade,
                ValorUnitario = i.PrecoUnitario,
                ValorTotal = i.Subtotal
            })
            .ToListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ProdutoRankingResumo>> ObterProdutosMaisVendidosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
        => ObterRankingAgregadoAsync(inicio, fim, decrescente: true, cancellationToken);

    public Task<IReadOnlyList<ProdutoRankingResumo>> ObterProdutosMenosVendidosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
        => ObterRankingAgregadoAsync(inicio, fim, decrescente: false, cancellationToken);

    public async Task<IReadOnlyList<ProdutoEncalhadoResumo>> ObterProdutosNuncaVendidosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        return await context.Produtos
            .AsNoTracking()
            .Where(p => p.Ativo && p.QuantidadeEstoque > 0)
            .Where(p => !context.ItensVenda.Any(i =>
                i.ProdutoId == p.Id &&
                i.Venda.Status == StatusVenda.Finalizada &&
                i.Venda.DataVenda >= inicio &&
                i.Venda.DataVenda <= fim))
            .Where(p => !context.ItensVendaExterna.Any(i =>
                i.ProdutoId == p.Id &&
                i.VendaExterna.DataVenda >= inicio &&
                i.VendaExterna.DataVenda <= fim))
            .OrderBy(p => p.Nome)
            .Select(p => new ProdutoEncalhadoResumo
            {
                CodigoInterno = p.CodigoInterno,
                NomeProduto = p.Nome,
                EstoqueAtual = p.QuantidadeEstoque,
                ValorTotalParado = p.QuantidadeEstoque * (p.Custo ?? 0m)
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ProdutoRankingResumo>> ObterRankingAgregadoAsync(
        DateTime inicio, DateTime fim, bool decrescente, CancellationToken cancellationToken)
    {
        await using var context = _contextFactory.CreateDbContext();

        var balcao = await context.ItensVenda
            .AsNoTracking()
            .Where(i => i.Venda.Status == StatusVenda.Finalizada &&
                        i.Venda.DataVenda >= inicio &&
                        i.Venda.DataVenda <= fim)
            .GroupBy(i => i.ProdutoId)
            .Select(g => new
            {
                ProdutoId = g.Key,
                Quantidade = g.Sum(x => x.Quantidade),
                Faturamento = g.Sum(x => x.Subtotal)
            })
            .ToListAsync(cancellationToken);

        var externas = await context.ItensVendaExterna
            .AsNoTracking()
            .Where(i => i.ProdutoId.HasValue &&
                        i.VendaExterna.DataVenda >= inicio &&
                        i.VendaExterna.DataVenda <= fim)
            .GroupBy(i => i.ProdutoId!.Value)
            .Select(g => new
            {
                ProdutoId = g.Key,
                Quantidade = g.Sum(x => x.Quantidade),
                Faturamento = g.Sum(x => x.Subtotal)
            })
            .ToListAsync(cancellationToken);

        if (balcao.Count == 0 && externas.Count == 0)
            return Array.Empty<ProdutoRankingResumo>();

        var agregado = balcao
            .Concat(externas)
            .GroupBy(x => x.ProdutoId)
            .Select(g => new
            {
                ProdutoId = g.Key,
                Quantidade = g.Sum(x => x.Quantidade),
                Faturamento = g.Sum(x => x.Faturamento)
            })
            .Where(x => x.Quantidade > 0)
            .ToList();

        if (agregado.Count == 0)
            return Array.Empty<ProdutoRankingResumo>();

        var ids = agregado.Select(a => a.ProdutoId).ToList();
        var produtos = await context.Produtos
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.CodigoInterno, p.Nome })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var ordenado = decrescente
            ? agregado.OrderByDescending(a => a.Quantidade).ThenByDescending(a => a.Faturamento)
            : agregado.OrderBy(a => a.Quantidade).ThenBy(a => a.Faturamento);

        return ordenado
            .Where(a => produtos.ContainsKey(a.ProdutoId))
            .Select(a => new ProdutoRankingResumo
            {
                CodigoInterno = produtos[a.ProdutoId].CodigoInterno,
                NomeProduto = produtos[a.ProdutoId].Nome,
                QuantidadeTotal = a.Quantidade,
                FaturamentoGerado = a.Faturamento
            })
            .ToList();
    }

    /// <summary>
    /// Itens vendidos no período nas duas frentes, com o canal de cada um. Duas consultas em
    /// vez de um UNION no banco: as fontes não têm o mesmo formato (o item de balcão sempre
    /// tem produto cadastrado, o de venda externa pode ser digitado à mão), e juntá-las em
    /// SQL exigiria igualar as duas formas antes de agrupar. O volume é o de um período de
    /// relatório, não o da tabela inteira.
    /// </summary>
    public async Task<IReadOnlyList<LinhaVendaPorCanalResumo>> ObterVendasPorCanalAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        // Vendas que vieram de pedido pago no site. Elas moram na MESMA tabela das vendas de
        // balcão — quem as distingue é o registro da integração. Sem esta consulta, toda
        // venda do site sairia rotulada como loja física, que é justamente o erro que este
        // relatório existe para não cometer.
        var vendasDoSite = await ObterIdsDeVendasDoSiteAsync(context, inicio, fim, cancellationToken);

        // IgnoreQueryFilters + Ativo explícito: o filtro global de soft-delete vale também
        // para Produto, e com ele um produto inativado hoje apagaria do relatório as vendas
        // que ele teve no período — o faturamento sairia menor do que foi. Histórico não
        // encolhe porque o item saiu de linha; quem precisa continuar fora são a venda e o
        // item cancelados.
        var balcao = await context.ItensVenda
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(i => i.Ativo && i.Venda.Ativo
                        && i.Venda.Status == StatusVenda.Finalizada
                        && i.Venda.DataVenda >= inicio && i.Venda.DataVenda <= fim)
            .Select(i => new LinhaVendaPorCanalResumo
            {
                DataVenda = i.Venda.DataVenda,
                // Preenchido abaixo, em memória: a classificação depende do schema da
                // integração, que não participa desta consulta.
                Canal = CanalVenda.LojaFisica,
                VendaId = i.VendaId,
                CodigoProduto = i.Produto.CodigoInterno,
                NomeProduto = i.Produto.Nome,
                NumeroVenda = i.Venda.NumeroVenda,
                Quantidade = i.Quantidade,
                ValorUnitario = i.PrecoUnitario,
                ValorTotal = i.Subtotal,
                ProdutoCadastrado = true
            })
            .ToListAsync(cancellationToken);

        foreach (var linha in balcao.Where(l => vendasDoSite.Contains(l.VendaId)))
            linha.Canal = CanalVenda.Site;

        var externas = await context.ItensVendaExterna
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(i => i.Ativo && i.VendaExterna.Ativo
                        && i.VendaExterna.DataVenda >= inicio && i.VendaExterna.DataVenda <= fim)
            .Select(i => new LinhaVendaPorCanalResumo
            {
                DataVenda = i.VendaExterna.DataVenda,
                // O módulo de venda externa é o da venda na rua: o site tem o próprio
                // caminho (ImperialSync), e o marketplace terá o dele.
                Canal = CanalVenda.VendaExternaRua,
                // Item manual não tem produto por trás: o nome foi digitado na hora.
                CodigoProduto = i.Produto != null ? i.Produto.CodigoInterno : string.Empty,
                NomeProduto = i.NomeProduto,
                NumeroVenda = i.VendaExterna.NumeroVendaExterna,
                Quantidade = i.Quantidade,
                ValorUnitario = i.PrecoUnitario,
                ValorTotal = i.Subtotal,
                ProdutoCadastrado = i.ProdutoId != null
            })
            .ToListAsync(cancellationToken);

        // Por código e depois por data: a pergunta do relatório é o que cada produto rendeu,
        // então as vendas do mesmo item precisam ficar juntas e em ordem cronológica. Item
        // manual (sem código) vai para o fim, para não abrir a listagem com linhas sem
        // código.
        return balcao
            .Concat(externas)
            .OrderByDescending(l => l.ProdutoCadastrado)
            .ThenBy(l => l.CodigoProduto, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.DataVenda)
            .ToList();
    }

    /// <summary>
    /// Ids das vendas do período que o ImperialSync criou a partir de pedidos do site.
    ///
    /// O schema <c>integration</c> é instalado à mão pelos scripts do ImperialSync e pode
    /// simplesmente não existir — loja que não vende pelo site nunca o instalou. Nesse caso
    /// (e no de o usuário do banco não poder lê-lo) o relatório segue sem canal "Site" em
    /// vez de estourar: não ter site é uma situação normal, não um erro.
    /// </summary>
    private static async Task<HashSet<int>> ObterIdsDeVendasDoSiteAsync(
        AppDbContext context, DateTime inicio, DateTime fim, CancellationToken cancellationToken)
    {
        try
        {
            var ids = await context.Database.SqlQuery<int>($"""
                SELECT o.local_sale_id AS "Value"
                FROM integration.imperial_sync_operations AS o
                JOIN public.vendas AS v ON v.id = o.local_sale_id
                WHERE o.operation_type = 'SALE_CREATE'
                  AND o.local_sale_id IS NOT NULL
                  AND v.data_venda >= {inicio} AND v.data_venda <= {fim}
                """).ToListAsync(cancellationToken);

            return ids.ToHashSet();
        }
        catch (Exception ex) when (VendaSiteRepository.ClassificarFalhaDeAcesso(ex) is not null)
        {
            return [];
        }
    }

    /// <summary>
    /// Itens vendidos no período agrupados por categoria, somando balcão e venda externa — as
    /// mesmas duas origens que o faturamento do Dashboard já soma.
    ///
    /// Produto e categoria entram sem o filtro global de soft-delete: inativar uma categoria
    /// hoje não pode fazer as vendas de ontem sumirem da contagem. A categoria é resolvida em
    /// memória a partir dos ids agregados, para o banco devolver poucas linhas.
    /// </summary>
    public async Task<IReadOnlyList<CategoriaItensVendidosResumo>> ObterItensVendidosPorCategoriaAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var balcao = await (
            from item in context.ItensVenda.AsNoTracking()
            join produto in context.Produtos.IgnoreQueryFilters().AsNoTracking()
                on item.ProdutoId equals produto.Id into correspondentes
            from produto in correspondentes.DefaultIfEmpty()
            where item.Venda.Status == StatusVenda.Finalizada &&
                  item.Venda.DataVenda >= inicio && item.Venda.DataVenda <= fim
            group item.Quantidade by (produto != null ? produto.CategoriaId : null) into g
            select new { CategoriaId = g.Key, Quantidade = g.Sum() })
            .ToListAsync(cancellationToken);

        var externas = await (
            from item in context.ItensVendaExterna.AsNoTracking()
            join produto in context.Produtos.IgnoreQueryFilters().AsNoTracking()
                on item.ProdutoId equals produto.Id into correspondentes
            from produto in correspondentes.DefaultIfEmpty()
            where item.VendaExterna.DataVenda >= inicio && item.VendaExterna.DataVenda <= fim
            group item.Quantidade by (produto != null ? produto.CategoriaId : null) into g
            select new { CategoriaId = g.Key, Quantidade = g.Sum() })
            .ToListAsync(cancellationToken);

        var porCategoria = balcao.Concat(externas)
            .GroupBy(x => x.CategoriaId)
            .Select(g => new { CategoriaId = g.Key, Quantidade = g.Sum(x => x.Quantidade) })
            .Where(x => x.Quantidade > 0)
            .ToList();

        if (porCategoria.Count == 0)
            return Array.Empty<CategoriaItensVendidosResumo>();

        var ids = porCategoria.Where(x => x.CategoriaId.HasValue).Select(x => x.CategoriaId!.Value).ToList();
        var nomes = await context.Categorias
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nome, cancellationToken);

        return porCategoria
            .Select(x => new CategoriaItensVendidosResumo
            {
                Categoria = x.CategoriaId.HasValue && nomes.TryGetValue(x.CategoriaId.Value, out var nome) ? nome : null,
                QuantidadeItens = x.Quantidade
            })
            .OrderByDescending(x => x.QuantidadeItens)
            .ThenBy(x => x.Categoria, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Top de produtos por quantidade vendida, cada um com em quantas vendas distintas
    /// apareceu. O mesmo produto lançado duas vezes na mesma venda conta como UMA venda —
    /// por isso o <c>Distinct</c> sobre o id da venda, e não a contagem de itens.
    ///
    /// Balcão e venda externa somam por produto; item manual da venda externa não tem produto
    /// e portanto não entra num ranking de produtos.
    /// </summary>
    public async Task<IReadOnlyList<ProdutoMaisVendidoResumo>> ObterProdutosMaisVendidosComVendasAsync(
        DateTime inicio, DateTime fim, int quantidade, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var balcao = await context.ItensVenda
            .AsNoTracking()
            .Where(i => i.Venda.Status == StatusVenda.Finalizada &&
                        i.Venda.DataVenda >= inicio && i.Venda.DataVenda <= fim)
            .GroupBy(i => i.ProdutoId)
            .Select(g => new
            {
                ProdutoId = g.Key,
                Quantidade = g.Sum(x => x.Quantidade),
                Vendas = g.Select(x => x.VendaId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        var externas = await context.ItensVendaExterna
            .AsNoTracking()
            .Where(i => i.ProdutoId.HasValue &&
                        i.VendaExterna.DataVenda >= inicio && i.VendaExterna.DataVenda <= fim)
            .GroupBy(i => i.ProdutoId!.Value)
            .Select(g => new
            {
                ProdutoId = g.Key,
                Quantidade = g.Sum(x => x.Quantidade),
                Vendas = g.Select(x => x.VendaExternaId).Distinct().Count()
            })
            .ToListAsync(cancellationToken);

        // Os ids de venda de balcão e de venda externa vêm de tabelas diferentes, então as
        // contagens de cada origem são somadas — nunca há a mesma venda nas duas.
        var top = balcao.Concat(externas)
            .GroupBy(x => x.ProdutoId)
            .Select(g => new
            {
                ProdutoId = g.Key,
                Quantidade = g.Sum(x => x.Quantidade),
                Vendas = g.Sum(x => x.Vendas)
            })
            .Where(x => x.Quantidade > 0)
            .OrderByDescending(x => x.Quantidade)
            .ThenByDescending(x => x.Vendas)
            .Take(Math.Max(1, quantidade))
            .ToList();

        if (top.Count == 0)
            return Array.Empty<ProdutoMaisVendidoResumo>();

        var ids = top.Select(x => x.ProdutoId).ToList();
        var produtos = await context.Produtos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.CodigoInterno, p.Nome })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return top
            .Where(x => produtos.ContainsKey(x.ProdutoId))
            .Select(x => new ProdutoMaisVendidoResumo
            {
                CodigoInterno = produtos[x.ProdutoId].CodigoInterno,
                NomeProduto = produtos[x.ProdutoId].Nome,
                QuantidadeVendida = x.Quantidade,
                QuantidadeVendas = x.Vendas
            })
            .ToList();
    }

    /// <summary>
    /// Extrato de entradas e saídas no período. Traz o documento de origem junto para a
    /// linha se explicar sozinha: "saída de 2 un" não diz nada, "saída de 2 un — venda
    /// 20260918-0001, Site" diz.
    /// </summary>
    public async Task<IReadOnlyList<LinhaMovimentacaoProdutoResumo>> ObterMovimentacoesProdutosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var linhas = await context.MovimentacoesEstoque
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(m => m.Ativo && m.CriadoEm >= inicio && m.CriadoEm <= fim)
            .Select(m => new LinhaMovimentacaoProdutoResumo
            {
                Data = m.CriadoEm,
                VendaId = m.VendaId,
                CodigoProduto = m.Produto.CodigoInterno,
                NomeProduto = m.Produto.Nome,
                Unidade = m.Produto.Unidade,
                Tipo = m.Tipo,
                Quantidade = m.Quantidade,
                SaldoAnterior = m.QuantidadeAnterior,
                SaldoPosterior = m.QuantidadeAtual,
                Motivo = m.Motivo,
                Usuario = m.Usuario,
                NumeroVenda = m.Venda != null
                    ? m.Venda.NumeroVenda
                    : (m.VendaExterna != null ? m.VendaExterna.NumeroVendaExterna : null),
                Canal = m.Venda != null
                    ? CanalVenda.LojaFisica
                    : (m.VendaExterna != null ? CanalVenda.VendaExternaRua : (CanalVenda?)null)
            })
            .ToListAsync(cancellationToken);

        // Mesma correção do relatório por canal: a baixa de uma venda do site sai de uma
        // venda da tabela de balcão, e sem cruzar com o registro da integração ela seria
        // descrita como saída de loja física.
        var vendasDoSite = await ObterIdsDeVendasDoSiteAsync(context, inicio, fim, cancellationToken);
        foreach (var linha in linhas.Where(l => l.VendaId is { } id && vendasDoSite.Contains(id)))
            linha.Canal = CanalVenda.Site;

        // Agrupado por produto e em ordem de data: é assim que se lê quando o item entrou e
        // em que dias ele saiu, que é o que o relatório existe para responder.
        return linhas
            .OrderBy(l => l.CodigoProduto, StringComparer.OrdinalIgnoreCase)
            .ThenBy(l => l.Data)
            .ToList();
    }
}
