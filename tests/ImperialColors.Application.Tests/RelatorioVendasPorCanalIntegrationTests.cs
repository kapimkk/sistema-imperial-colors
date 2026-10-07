using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// A parte do relatório por canal que só o banco de verdade prova: a venda do site é gravada
/// na MESMA tabela das vendas de balcão, e quem as distingue é o registro da integração
/// (<c>integration.imperial_sync_operations</c>) — não há coluna nenhuma em <c>vendas</c>
/// dizendo de onde o pedido veio.
///
/// Sem este cruzamento, todo pedido do site sairia somado em "Loja física" e o relatório
/// responderia errado exatamente a pergunta que existe para responder. É um erro que nenhum
/// teste com repositório mockado pegaria, porque o mock devolveria o canal já pronto.
///
/// Segue o padrão dos outros testes de integração: tudo dentro de uma transação revertida no
/// fim, e sem o schema <c>integration</c> instalado o teste termina sem fazer nada.
/// </summary>
[Collection(SemParalelismoCollection.Nome)]
public class RelatorioVendasPorCanalIntegrationTests
{
    private static string NovoMarcador() => "TC" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<bool> IntegracaoInstaladaAsync(AppDbContext ctx)
        => await ctx.Database
            .SqlQuery<bool>($"SELECT to_regclass('integration.imperial_sync_operations') IS NOT NULL AS \"Value\"")
            .SingleAsync();

    private static Venda NovaVenda(string numero, decimal total, int produtoId)
    {
        var venda = new Venda
        {
            NumeroVenda = numero,
            Status = StatusVenda.Finalizada,
            Subtotal = total,
            Total = total,
            FormaPagamento = FormaPagamento.Pix,
            QuantidadeParcelas = 1,
            ValorPago = total,
            NomeCompradorCupom = "Consumidor Final",
            DataVenda = DateTime.Now
        };
        venda.Itens.Add(new ItemVenda
        {
            ProdutoId = produtoId,
            Quantidade = 1m,
            PrecoUnitario = total,
            Subtotal = total
        });
        return venda;
    }

    private static Task RegistrarNaIntegracaoAsync(AppDbContext ctx, string pedido, int vendaId, string numeroVenda)
        => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO integration.imperial_sync_operations
                (operation_id, site_order_id, site_order_number, payload_sha256, local_sale_id,
                 local_sale_number, agent_id, result, site_paid_at, processed_at)
            VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {pedido}, {new string('a', 64)}, {vendaId},
                    {numeroVenda}, 'teste-canal', jsonb_build_object(),
                    {DateTimeOffset.Now.AddMinutes(-3)}, {DateTimeOffset.Now})
            """);

    [Fact]
    public async Task VendaDoSite_NaoEhContadaComoLojaFisica()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var conexao))
            return;

        var opcoes = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conexao).Options;
        await using var ctx = new AppDbContext(opcoes);
        await using var tx = await ctx.Database.BeginTransactionAsync();

        if (!await IntegracaoInstaladaAsync(ctx))
            return;

        var marcador = NovoMarcador();

        var categoria = new Categoria { Nome = $"{marcador} Categoria" };
        var marca = new Marca { Nome = $"{marcador} Marca" };
        ctx.Categorias.Add(categoria);
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();

        var produto = new Produto
        {
            CodigoInterno = $"{marcador}-01",
            Nome = $"{marcador} Tinta",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            QuantidadeEstoque = 100m,
            PrecoVenda = 100m
        };
        ctx.Produtos.Add(produto);
        await ctx.SaveChangesAsync();

        // Duas vendas idênticas na tabela de vendas. A única diferença entre elas é que uma
        // tem registro na integração — é exatamente isso que o relatório precisa enxergar.
        var doSite = NovaVenda($"{marcador}-S", 150m, produto.Id);
        var doBalcao = NovaVenda($"{marcador}-B", 90m, produto.Id);
        ctx.Vendas.AddRange(doSite, doBalcao);
        await ctx.SaveChangesAsync();

        await RegistrarNaIntegracaoAsync(ctx, $"IC-{marcador}-1", doSite.Id, doSite.NumeroVenda);

        var repositorio = new RelatorioAnalyticsRepository(new FactoryDeContextoFixo(ctx));
        var linhas = await repositorio.ObterVendasPorCanalAsync(
            DateTime.Today, DateTime.Today.AddDays(1).AddSeconds(-1));

        var site = linhas.Single(l => l.NumeroVenda == doSite.NumeroVenda);
        var balcao = linhas.Single(l => l.NumeroVenda == doBalcao.NumeroVenda);

        Assert.Equal(CanalVenda.Site, site.Canal);
        Assert.Equal(CanalVenda.LojaFisica, balcao.Canal);
        Assert.Equal(produto.CodigoInterno, site.CodigoProduto);
        Assert.Equal(150m, site.ValorTotal);

        await tx.RollbackAsync();
    }

    /// <summary>
    /// A saída de estoque de um pedido do site nasce de uma venda da tabela de balcão. Sem o
    /// mesmo cruzamento, o extrato diria que o produto saiu pela loja física.
    /// </summary>
    [Fact]
    public async Task Movimentacao_SaidaDeVendaDoSite_AparecaComoSite()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var conexao))
            return;

        var opcoes = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conexao).Options;
        await using var ctx = new AppDbContext(opcoes);
        await using var tx = await ctx.Database.BeginTransactionAsync();

        if (!await IntegracaoInstaladaAsync(ctx))
            return;

        var marcador = NovoMarcador();

        var categoria = new Categoria { Nome = $"{marcador} Categoria" };
        var marca = new Marca { Nome = $"{marcador} Marca" };
        ctx.Categorias.Add(categoria);
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();

        var produto = new Produto
        {
            CodigoInterno = $"{marcador}-01",
            Nome = $"{marcador} Tinta",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            QuantidadeEstoque = 100m,
            PrecoVenda = 100m
        };
        ctx.Produtos.Add(produto);
        await ctx.SaveChangesAsync();

        var venda = NovaVenda($"{marcador}-S", 150m, produto.Id);
        ctx.Vendas.Add(venda);
        await ctx.SaveChangesAsync();

        ctx.MovimentacoesEstoque.Add(new MovimentacaoEstoque
        {
            ProdutoId = produto.Id,
            Tipo = TipoMovimentacao.Saida,
            Quantidade = 1m,
            QuantidadeAnterior = 100m,
            QuantidadeAtual = 99m,
            Motivo = "Venda",
            VendaId = venda.Id
        });
        await ctx.SaveChangesAsync();

        await RegistrarNaIntegracaoAsync(ctx, $"IC-{marcador}-1", venda.Id, venda.NumeroVenda);

        var repositorio = new RelatorioAnalyticsRepository(new FactoryDeContextoFixo(ctx));
        var linhas = await repositorio.ObterMovimentacoesProdutosAsync(
            DateTime.Today, DateTime.Today.AddDays(1).AddSeconds(-1));

        var saida = linhas.Single(l => l.CodigoProduto == produto.CodigoInterno);

        Assert.Equal(CanalVenda.Site, saida.Canal);
        Assert.Equal(venda.NumeroVenda, saida.NumeroVenda);

        await tx.RollbackAsync();
    }
}
