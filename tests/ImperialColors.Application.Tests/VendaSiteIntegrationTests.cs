using ImperialColors.Application.DTOs;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.ReadModels;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// "Vendas Site" contra o PostgreSQL de verdade e o schema <c>integration</c> instalado pelos
/// scripts SQL do ImperialSync: a lista nasce do registro da integração e só mostra o que veio do
/// site; venda de balcão nunca aparece.
///
/// Segue o padrão dos outros testes de integração: tudo dentro de uma transação que é revertida
/// no fim (nada fica no banco de desenvolvimento) e, sem o schema <c>integration</c> instalado,
/// os testes terminam sem fazer nada — o ImperialSync é opcional para quem desenvolve o sistema.
/// </summary>
[Collection(SemParalelismoCollection.Nome)]
public class VendaSiteIntegrationTests
{
    private sealed class Cenario
    {
        public required AppDbContext Contexto { get; init; }
        public required string Marcador { get; init; }
        public required Venda Pix { get; init; }
        public required Venda Cartao { get; init; }
        public required Venda ComCadastro { get; init; }
        public required Venda Cancelada { get; init; }
        public required Venda Inativada { get; init; }
        public required Venda Balcao { get; init; }
        public required string NumeroDaVendaRemovida { get; init; }
        public required Cliente ClienteCadastrado { get; init; }
        public required DateTime Base { get; init; }
    }

    private static string NovoMarcador() => "TSITE" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<bool> IntegracaoInstaladaAsync(AppDbContext ctx)
        => await ctx.Database
            .SqlQuery<bool>($"SELECT to_regclass('integration.imperial_sync_operations') IS NOT NULL AS \"Value\"")
            .SingleAsync();

    private static Venda NovaVenda(
        string numero, string? cupom, int? clienteId, FormaPagamento forma, int parcelas, decimal total,
        StatusVenda status = StatusVenda.Finalizada, bool ativo = true, string? usuario = "ecommerce")
    {
        var venda = new Venda
        {
            NumeroVenda = numero,
            Status = status,
            Subtotal = total,
            Total = total,
            FormaPagamento = forma,
            QuantidadeParcelas = parcelas,
            ValorPago = total,
            NomeCompradorCupom = cupom,
            ClienteId = clienteId,
            Usuario = usuario,
            DataVenda = DateTime.Now,
            Ativo = ativo
        };
        venda.Pagamentos.Add(new VendaPagamento { FormaPagamento = forma, Valor = total, QuantidadeParcelas = parcelas, Ordem = 1 });
        return venda;
    }

    private static Task RegistrarNaIntegracaoAsync(AppDbContext ctx, string pedido, int vendaId, string numeroVenda, DateTimeOffset sincronizadoEm)
        => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO integration.imperial_sync_operations
                (operation_id, site_order_id, site_order_number, payload_sha256, local_sale_id,
                 local_sale_number, agent_id, result, site_paid_at, processed_at)
            VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {pedido}, {new string('a', 64)}, {vendaId},
                    {numeroVenda}, 'teste-vendas-site', jsonb_build_object(), {sincronizadoEm.AddMinutes(-3)}, {sincronizadoEm})
            """);

    /// <summary>
    /// Seis vendas do site (pix, cartão em 3x, comprador cadastrado, cancelada, inativada e uma cuja
    /// venda foi apagada depois) e UMA venda de balcão com o mesmo marcador no nome, que não
    /// pode aparecer. A "mais recente" é a do pix; a mais antiga, a removida.
    /// </summary>
    private static async Task<Cenario> MontarAsync(AppDbContext ctx)
    {
        var marcador = NovoMarcador();
        var agora = DateTime.Now;

        var cliente = new Cliente { Nome = $"{marcador} Cliente Cadastrado" };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var pix = NovaVenda($"{marcador}-P", $"{marcador} Comprador Pix", null, FormaPagamento.Pix, 1, 365.47m);
        var cartao = NovaVenda($"{marcador}-C", $"{marcador} Comprador Cartao", null, FormaPagamento.CartaoCredito, 3, 199.99m);
        var comCadastro = NovaVenda($"{marcador}-D", null, cliente.Id, FormaPagamento.Boleto, 1, 80m);
        var cancelada = NovaVenda($"{marcador}-X", $"{marcador} Comprador Cancelada", null, FormaPagamento.Pix, 1, 50m, StatusVenda.Cancelada);
        var inativada = NovaVenda($"{marcador}-I", $"{marcador} Comprador Inativada", null, FormaPagamento.Pix, 1, 60m, ativo: false);
        var balcao = NovaVenda($"{marcador}-B", $"{marcador} Comprador Balcao", null, FormaPagamento.Dinheiro, 1, 10m, usuario: "caixa");

        ctx.Set<Venda>().AddRange(pix, cartao, comCadastro, cancelada, inativada, balcao);
        await ctx.SaveChangesAsync();

        // O registro da integração não tem chave estrangeira para a venda: aponta para um id que
        // nunca existiu, como acontece quando alguém exclui a venda depois.
        var idInexistente = 2_000_000_000 + Random.Shared.Next(0, 1_000_000);
        var numeroRemovida = $"{marcador}-R";

        var maisRecente = new DateTimeOffset(agora);
        await RegistrarNaIntegracaoAsync(ctx, $"IC-{marcador}-1", pix.Id, pix.NumeroVenda, maisRecente);
        await RegistrarNaIntegracaoAsync(ctx, $"IC-{marcador}-2", cartao.Id, cartao.NumeroVenda, maisRecente.AddMinutes(-1));
        await RegistrarNaIntegracaoAsync(ctx, $"IC-{marcador}-3", comCadastro.Id, comCadastro.NumeroVenda, maisRecente.AddMinutes(-2));
        await RegistrarNaIntegracaoAsync(ctx, $"IC-{marcador}-4", cancelada.Id, cancelada.NumeroVenda, maisRecente.AddMinutes(-3));
        await RegistrarNaIntegracaoAsync(ctx, $"IC-{marcador}-5", inativada.Id, inativada.NumeroVenda, maisRecente.AddMinutes(-4));
        await RegistrarNaIntegracaoAsync(ctx, $"IC-{marcador}-6", idInexistente, numeroRemovida, maisRecente.AddMinutes(-5));

        return new Cenario
        {
            Contexto = ctx, Marcador = marcador, Pix = pix, Cartao = cartao, ComCadastro = comCadastro,
            Cancelada = cancelada, Inativada = inativada, Balcao = balcao,
            NumeroDaVendaRemovida = numeroRemovida, ClienteCadastrado = cliente, Base = agora
        };
    }

    /// <summary>Abre o contexto numa transação e entrega o serviço lendo por dentro dela. Devolve
    /// <c>null</c> quando a integração não está instalada neste banco.</summary>
    private static async Task<(AppDbContext Contexto, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction Transacao, Cenario Cenario, VendaSiteService Servico, VendaSiteRepository Repositorio)?> PrepararAsync()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var conexao))
            return null;

        var opcoes = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conexao).Options;
        var ctx = new AppDbContext(opcoes);
        var tx = await ctx.Database.BeginTransactionAsync();

        if (!await IntegracaoInstaladaAsync(ctx))
        {
            await tx.RollbackAsync();
            await tx.DisposeAsync();
            await ctx.DisposeAsync();
            return null;
        }

        var cenario = await MontarAsync(ctx);
        var repositorio = new VendaSiteRepository(new FactoryDeContextoFixo(ctx));
        return (ctx, tx, cenario, new VendaSiteService(repositorio), repositorio);
    }

    private static async Task EncerrarAsync(AppDbContext ctx, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
    {
        await tx.RollbackAsync();
        await tx.DisposeAsync();
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Lista_SoTemVendasDoSite_VendaDeBalcaoNuncaAparece()
    {
        if (await PrepararAsync() is not var (ctx, tx, c, servico, _))
            return;

        try
        {
            var resultado = await servico.ObterPaginadoAsync(1, 50, c.Marcador);

            Assert.Equal(SituacaoIntegracaoSite.Disponivel, resultado.Situacao);
            var numeros = resultado.Pagina.Itens.Select(i => i.NumeroVenda).ToList();
            Assert.Equal(6, resultado.Pagina.TotalItens);
            Assert.Equal(6, numeros.Count);
            Assert.DoesNotContain(c.Balcao.NumeroVenda, numeros);
            Assert.All(resultado.Pagina.Itens, item => Assert.StartsWith($"IC-{c.Marcador}-", item.PedidoSite));
        }
        finally
        {
            await EncerrarAsync(ctx, tx);
        }
    }

    [Fact]
    public async Task Linhas_TraemOQueOOperadorPrecisaConferir()
    {
        if (await PrepararAsync() is not var (ctx, tx, c, servico, _))
            return;

        try
        {
            var itens = (await servico.ObterPaginadoAsync(1, 50, c.Marcador)).Pagina.Itens;
            VendaSiteDto Por(string sufixo) => itens.Single(i => i.PedidoSite == $"IC-{c.Marcador}-{sufixo}");

            var pix = Por("1");
            Assert.Equal(c.Pix.NumeroVenda, pix.NumeroVenda);
            Assert.Equal(c.Pix.Id, pix.VendaId);
            Assert.Equal($"{c.Marcador} Comprador Pix", pix.Cliente);
            Assert.Equal(365.47m, pix.Total);
            Assert.Equal("Pix", pix.Pagamento);
            Assert.Equal(1, pix.Parcelas);
            Assert.Equal("Finalizada", pix.StatusDescricao);
            Assert.True(pix.VendaExiste);
            Assert.NotNull(pix.DataVenda);
            Assert.True(Math.Abs((pix.SincronizadoEm - c.Base).TotalMinutes) < 5, "data da sincronização fora do esperado");

            var cartao = Por("2");
            Assert.Equal("Cartão de Crédito - 3x", cartao.Pagamento);
            Assert.Equal(3, cartao.Parcelas);
            Assert.Equal("3x", cartao.ParcelasDescricao);

            // Sem nome no cupom, vale o nome do cliente cadastrado.
            var cadastrado = Por("3");
            Assert.Equal($"{c.Marcador} Cliente Cadastrado", cadastrado.Cliente);
            Assert.Equal("Boleto", cadastrado.Pagamento);

            Assert.Equal("Cancelada", Por("4").StatusDescricao);
            Assert.Equal("Excluída", Por("5").StatusDescricao);

            var removida = Por("6");
            Assert.Equal("Venda removida", removida.StatusDescricao);
            Assert.Equal(c.NumeroDaVendaRemovida, removida.NumeroVenda);
            Assert.False(removida.VendaExiste);
            Assert.Equal("—", removida.Cliente);
            Assert.Equal("—", removida.TotalDescricao);
            Assert.Equal("—", removida.Pagamento);
        }
        finally
        {
            await EncerrarAsync(ctx, tx);
        }
    }

    [Fact]
    public async Task Ordem_DaSincronizacaoMaisRecenteParaAMaisAntiga()
    {
        if (await PrepararAsync() is not var (ctx, tx, c, servico, _))
            return;

        try
        {
            var itens = (await servico.ObterPaginadoAsync(1, 50, c.Marcador)).Pagina.Itens;

            Assert.Equal(
                Enumerable.Range(1, 6).Select(n => $"IC-{c.Marcador}-{n}").ToList(),
                itens.Select(i => i.PedidoSite).ToList());
            Assert.Equal(itens.OrderByDescending(i => i.SincronizadoEm).Select(i => i.PedidoSite), itens.Select(i => i.PedidoSite));
        }
        finally
        {
            await EncerrarAsync(ctx, tx);
        }
    }

    [Fact]
    public async Task Busca_PorPedido_PorNumeroDaVenda_PorNomeDoCupom_EPorNomeDoCliente()
    {
        if (await PrepararAsync() is not var (ctx, tx, c, servico, _))
            return;

        try
        {
            async Task<List<string>> Buscar(string termo)
                => (await servico.ObterPaginadoAsync(1, 50, termo)).Pagina.Itens.Select(i => i.PedidoSite).ToList();

            Assert.Equal([$"IC-{c.Marcador}-2"], await Buscar($"IC-{c.Marcador}-2"));
            Assert.Equal([$"IC-{c.Marcador}-1"], await Buscar(c.Pix.NumeroVenda));
            Assert.Equal([$"IC-{c.Marcador}-6"], await Buscar(c.NumeroDaVendaRemovida));
            Assert.Equal([$"IC-{c.Marcador}-2"], await Buscar($"{c.Marcador} Comprador Cartao"));
            Assert.Equal([$"IC-{c.Marcador}-3"], await Buscar($"{c.Marcador} Cliente Cadastrado"));
            // Sem diferenciar maiúsculas de minúsculas.
            Assert.Equal([$"IC-{c.Marcador}-2"], await Buscar($"{c.Marcador} COMPRADOR cartao".ToLowerInvariant()));
            Assert.Empty(await Buscar($"{c.Marcador}-nada-assim"));
            // O comprador do balcão tem o marcador no nome, mas a venda não é do site.
            Assert.DoesNotContain($"{c.Marcador} Comprador Balcao", (await servico.ObterPaginadoAsync(1, 50, "Balcao")).Pagina.Itens.Select(i => i.Cliente));
        }
        finally
        {
            await EncerrarAsync(ctx, tx);
        }
    }

    [Fact]
    public async Task Busca_NaoTrataPorcentoESublinhadoComoCuringa()
    {
        if (await PrepararAsync() is not var (ctx, tx, c, servico, _))
            return;

        try
        {
            var resultado = await servico.ObterPaginadoAsync(1, 50, "%");
            Assert.DoesNotContain(resultado.Pagina.Itens, i => i.PedidoSite.StartsWith($"IC-{c.Marcador}", StringComparison.Ordinal));

            // "_" casaria com qualquer caractere se fosse curinga: o marcador seguido de "_P"
            // não existe (o número é "...-P").
            Assert.Empty((await servico.ObterPaginadoAsync(1, 50, $"{c.Marcador}_P")).Pagina.Itens);
        }
        finally
        {
            await EncerrarAsync(ctx, tx);
        }
    }

    [Fact]
    public async Task Paginacao_NoBanco_ComTotalCerto()
    {
        if (await PrepararAsync() is not var (ctx, tx, c, servico, _))
            return;

        try
        {
            var primeira = await servico.ObterPaginadoAsync(1, 4, c.Marcador);
            var segunda = await servico.ObterPaginadoAsync(2, 4, c.Marcador);
            var terceira = await servico.ObterPaginadoAsync(3, 4, c.Marcador);

            Assert.Equal(6, primeira.Pagina.TotalItens);
            Assert.Equal(2, primeira.Pagina.TotalPaginas);
            Assert.Equal(4, primeira.Pagina.Itens.Count);
            Assert.Equal(2, segunda.Pagina.Itens.Count);
            Assert.Empty(terceira.Pagina.Itens);
            Assert.Equal(6, terceira.Pagina.TotalItens);

            var todos = primeira.Pagina.Itens.Concat(segunda.Pagina.Itens).Select(i => i.PedidoSite).ToList();
            Assert.Equal(6, todos.Distinct().Count());
        }
        finally
        {
            await EncerrarAsync(ctx, tx);
        }
    }

    [Fact]
    public async Task SemBusca_ListaTudoQueFoiSincronizado_ComOsDadosDoCenarioNoTopo()
    {
        if (await PrepararAsync() is not var (ctx, tx, c, _, repositorio))
            return;

        try
        {
            var pagina = await repositorio.ObterPaginadoAsync(1, 200);

            Assert.Equal(SituacaoIntegracaoSite.Disponivel, pagina.Situacao);
            Assert.True(pagina.Total >= 6);
            // As seis do cenário são as mais recentes: entram no começo da lista.
            Assert.Contains(pagina.Itens, i => i.PedidoSite == $"IC-{c.Marcador}-1");
            Assert.Equal($"IC-{c.Marcador}-1", pagina.Itens[0].PedidoSite);
        }
        finally
        {
            await EncerrarAsync(ctx, tx);
        }
    }

    [Fact]
    public async Task BancoSemOSchemaIntegration_DizQueNaoEstaInstalada_SemLancar()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var conexao))
            return;

        // O banco administrativo "postgres" existe em qualquer servidor PostgreSQL e nunca tem o
        // schema da integração: é o caso do cliente que ainda não executou os scripts.
        var semIntegracao = new NpgsqlConnectionStringBuilder(conexao) { Database = "postgres", Pooling = false }.ConnectionString;
        var opcoes = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(semIntegracao).Options;
        await using var ctx = new AppDbContext(opcoes);
        var repositorio = new VendaSiteRepository(new FactoryDeContextoFixo(ctx));

        var pagina = await repositorio.ObterPaginadoAsync(1, 50);

        Assert.Equal(SituacaoIntegracaoSite.NaoInstalada, pagina.Situacao);
        Assert.Empty(pagina.Itens);
        Assert.Equal(0, pagina.Total);

        var comBusca = await repositorio.ObterPaginadoAsync(1, 50, "qualquer");
        Assert.Equal(SituacaoIntegracaoSite.NaoInstalada, comBusca.Situacao);
    }

    [Fact]
    public async Task SistemaNaoGravaNada_AConsultaEhSoLeitura()
    {
        if (await PrepararAsync() is not var (ctx, tx, c, servico, _))
            return;

        try
        {
            var antes = await ctx.Database.SqlQuery<long>($"""
                SELECT (SELECT count(*) FROM public.vendas)
                     + (SELECT count(*) FROM public.venda_pagamentos)
                     + (SELECT count(*) FROM integration.imperial_sync_operations) AS "Value"
                """).SingleAsync();

            await servico.ObterPaginadoAsync(1, 50, c.Marcador);
            await servico.ObterPaginadoAsync(2, 2);
            await servico.ObterPaginadoAsync(1, 50);

            var depois = await ctx.Database.SqlQuery<long>($"""
                SELECT (SELECT count(*) FROM public.vendas)
                     + (SELECT count(*) FROM public.venda_pagamentos)
                     + (SELECT count(*) FROM integration.imperial_sync_operations) AS "Value"
                """).SingleAsync();

            Assert.Equal(antes, depois);
        }
        finally
        {
            await EncerrarAsync(ctx, tx);
        }
    }
}
