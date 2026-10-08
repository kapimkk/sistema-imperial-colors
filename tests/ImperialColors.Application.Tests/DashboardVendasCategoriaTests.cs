using ImperialColors.Application.DTOs;
using ImperialColors.Application.Services;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Domain.ReadModels;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Os dois blocos novos da aba Vendas do Dashboard: itens vendidos por categoria e os cinco
/// produtos que mais saíram, cada um com a quantidade de vendas em que apareceu.
/// </summary>
public class DashboardVendasCategoriaTests
{
    private readonly Mock<IRelatorioAnalyticsRepository> _repository = new();

    private RelatorioAnalyticsService CriarServico() => new(
        _repository.Object,
        new Mock<IVendaService>().Object,
        new Mock<IVendaExternaService>().Object);

    [Fact]
    public async Task Categorias_ChegamOrdenadasEComABarraProporcionalAMaior()
    {
        _repository
            .Setup(r => r.ObterItensVendidosPorCategoriaAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CategoriaItensVendidosResumo { Categoria = "Tintas", QuantidadeItens = 40m },
                new CategoriaItensVendidosResumo { Categoria = "Pincéis", QuantidadeItens = 10m }
            ]);

        var categorias = await CriarServico().ObterItensVendidosPorCategoriaAsync(DateTime.Today, DateTime.Today);

        Assert.Equal(100m, categorias[0].PercentualBarra);
        Assert.Equal(25m, categorias[1].PercentualBarra);
    }

    /// <summary>Produto sem categoria e item digitado à mão na venda externa chegam sem nome de
    /// categoria. Têm que aparecer (o total de itens vendidos precisa bater), com um nome que
    /// o lojista entenda — célula em branco parece falha de carregamento.</summary>
    [Fact]
    public async Task Categorias_ItemSemCategoriaApareceComoSemCategoria()
    {
        _repository
            .Setup(r => r.ObterItensVendidosPorCategoriaAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CategoriaItensVendidosResumo { Categoria = null, QuantidadeItens = 3m }]);

        var categorias = await CriarServico().ObterItensVendidosPorCategoriaAsync(DateTime.Today, DateTime.Today);

        Assert.Equal("Sem categoria", categorias.Single().Categoria);
    }

    [Fact]
    public async Task Categorias_SemVendaNoMes_DevolveListaVaziaSemDividirPorZero()
    {
        _repository
            .Setup(r => r.ObterItensVendidosPorCategoriaAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CategoriaItensVendidosResumo>());

        Assert.Empty(await CriarServico().ObterItensVendidosPorCategoriaAsync(DateTime.Today, DateTime.Today));
    }

    [Fact]
    public async Task MaisVendidos_NumeraAPosicaoELevaAQuantidadeDeVendas()
    {
        _repository
            .Setup(r => r.ObterProdutosMaisVendidosComVendasAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ProdutoMaisVendidoResumo { CodigoInterno = "TAB-001", NomeProduto = "Tinta Branca", QuantidadeVendida = 40m, QuantidadeVendas = 12 },
                new ProdutoMaisVendidoResumo { CodigoInterno = "PIN-002", NomeProduto = "Pincel", QuantidadeVendida = 30m, QuantidadeVendas = 30 }
            ]);

        var top = await CriarServico().ObterProdutosMaisVendidosComVendasAsync(DateTime.Today, DateTime.Today, 5);

        Assert.Equal([1, 2], top.Select(t => t.Posicao).ToArray());
        Assert.Equal(12, top[0].QuantidadeVendas);
        Assert.Equal(40m, top[0].QuantidadeVendida);
    }

    private (DashboardService Servico, Mock<IRelatorioAnalyticsService> Analytics) CriarDashboard()
    {
        var analytics = new Mock<IRelatorioAnalyticsService>();
        analytics
            .Setup(a => a.ObterItensVendidosPorCategoriaAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CategoriaItensVendidosDto { Categoria = "Tintas", QuantidadeItens = 7m }]);
        analytics
            .Setup(a => a.ObterProdutosMaisVendidosComVendasAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ProdutoMaisVendidoDto { Posicao = 1, NomeProduto = "Tinta", QuantidadeVendida = 7m, QuantidadeVendas = 3 }]);

        var servico = new DashboardService(
            new Mock<IVendaRepository>().Object, new Mock<IVendaExternaRepository>().Object,
            new Mock<IVendaExternaService>().Object,
            new Mock<IProdutoRepository>().Object,
            new Mock<IProdutoService>().Object,
            analytics.Object);
        return (servico, analytics);
    }

    [Fact]
    public async Task ProdutosVendidos_TrazOsDoisBlocosLimitadosACinco()
    {
        var (dashboard, _) = CriarDashboard();

        var visao = await dashboard.ObterProdutosVendidosAsync(PeriodoDashboard.Mes);

        Assert.Equal("Tintas", visao.ItensPorCategoria.Single().Categoria);
        Assert.Equal(3, visao.ProdutosMaisVendidos.Single().QuantidadeVendas);
        Assert.Equal(PeriodoDashboard.Mes, visao.Periodo);
    }

    /// <summary>Mês cobre do dia 1 ao último instante do mês corrente.</summary>
    [Fact]
    public async Task ProdutosVendidos_Mes_ConsultaOMesCorrente()
    {
        var (dashboard, analytics) = CriarDashboard();

        await dashboard.ObterProdutosVendidosAsync(PeriodoDashboard.Mes);

        var hoje = DateTime.Today;
        var inicioMes = new DateTime(hoje.Year, hoje.Month, 1);
        analytics.Verify(a => a.ObterItensVendidosPorCategoriaAsync(
            inicioMes, inicioMes.AddMonths(1).AddTicks(-1), It.IsAny<CancellationToken>()), Times.Once);
        analytics.Verify(a => a.ObterProdutosMaisVendidosComVendasAsync(
            inicioMes, inicioMes.AddMonths(1).AddTicks(-1), 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Total tem que alcançar vendas de meses anteriores — se começasse no mês
    /// corrente, "Total" seria só outro nome para "Mês". O fim é hoje: venda não nasce no
    /// futuro.</summary>
    [Fact]
    public async Task ProdutosVendidos_Total_ComecaMuitoAntesDoMesCorrenteEVaiAteHoje()
    {
        var (dashboard, analytics) = CriarDashboard();
        DateTime inicioUsado = default, fimUsado = default;
        analytics
            .Setup(a => a.ObterItensVendidosPorCategoriaAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, CancellationToken>((i, f, _) => { inicioUsado = i; fimUsado = f; })
            .ReturnsAsync(Array.Empty<CategoriaItensVendidosDto>());

        var visao = await dashboard.ObterProdutosVendidosAsync(PeriodoDashboard.Total);

        Assert.Equal(PeriodoDashboard.Total, visao.Periodo);
        Assert.True(inicioUsado <= new DateTime(2001, 1, 1), $"início {inicioUsado:d} não cobre o histórico");
        Assert.Equal(DateTime.Today.AddDays(1).AddTicks(-1), fimUsado);
        // Longe do MinValue: o Npgsql o reinterpreta como "-infinity".
        Assert.NotEqual(DateTime.MinValue, inicioUsado);
    }

    // ---------- Banco de verdade ----------

    /// <summary>
    /// A agregação em SQL, que um repositório mockado não prova: soma de balcão e venda
    /// externa, "mesmo produto duas vezes na mesma venda conta uma venda só", venda cancelada
    /// fora, e item manual caindo em "sem categoria".
    /// </summary>
    [Fact]
    public async Task Banco_AgregaPorCategoriaEContaVendasDistintas()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var conexao))
            return;

        var opcoes = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conexao).Options;
        await using var ctx = new AppDbContext(opcoes);
        await using var tx = await ctx.Database.BeginTransactionAsync();

        var m = "TD" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var categoria = new Categoria { Nome = $"{m} Cat" };
        var marca = new Marca { Nome = $"{m} Marca" };
        ctx.Categorias.Add(categoria);
        ctx.Marcas.Add(marca);
        await ctx.SaveChangesAsync();

        var produto = new Produto
        {
            CodigoInterno = $"{m}-01", Nome = $"{m} Tinta",
            CategoriaId = categoria.Id, MarcaId = marca.Id,
            QuantidadeEstoque = 100m, PrecoVenda = 10m
        };
        ctx.Produtos.Add(produto);
        await ctx.SaveChangesAsync();

        Venda NovaVenda(string numero, StatusVenda status, params (decimal Qtd, decimal Preco)[] itens)
        {
            var total = itens.Sum(i => i.Qtd * i.Preco);
            var venda = new Venda
            {
                NumeroVenda = numero, Status = status, Subtotal = total, Total = total,
                FormaPagamento = FormaPagamento.Pix, QuantidadeParcelas = 1, ValorPago = total,
                NomeCompradorCupom = "Consumidor Final", DataVenda = DateTime.Now
            };
            foreach (var (qtd, preco) in itens)
                venda.Itens.Add(new ItemVenda { ProdutoId = produto.Id, Quantidade = qtd, PrecoUnitario = preco, Subtotal = qtd * preco });
            return venda;
        }

        // Venda A: o mesmo produto lançado duas vezes (2 + 3) — uma venda, cinco unidades.
        ctx.Vendas.Add(NovaVenda($"{m}-A", StatusVenda.Finalizada, (2m, 10m), (3m, 10m)));
        // Venda B: quatro unidades. Venda C cancelada: não pode contar.
        ctx.Vendas.Add(NovaVenda($"{m}-B", StatusVenda.Finalizada, (4m, 10m)));
        ctx.Vendas.Add(NovaVenda($"{m}-C", StatusVenda.Cancelada, (100m, 10m)));

        var externa = new VendaExterna { NumeroVendaExterna = $"{m}-E", DataVenda = DateTime.Now };
        externa.Itens.Add(new ItemVendaExterna { ProdutoId = produto.Id, NomeProduto = produto.Nome, Quantidade = 6m, PrecoUnitario = 10m, Subtotal = 60m });
        externa.Itens.Add(new ItemVendaExterna { ProdutoId = null, NomeProduto = $"{m} Item manual", Quantidade = 2m, PrecoUnitario = 5m, Subtotal = 10m });
        externa.CalcularTotais();
        ctx.VendasExternas.Add(externa);
        await ctx.SaveChangesAsync();

        var repositorio = new RelatorioAnalyticsRepository(new FactoryDeContextoFixo(ctx));
        var inicio = DateTime.Today;
        var fim = DateTime.Today.AddDays(1).AddSeconds(-1);

        var categorias = await repositorio.ObterItensVendidosPorCategoriaAsync(inicio, fim);
        var daCategoria = categorias.Single(c => c.Categoria == $"{m} Cat");
        // 5 (venda A) + 4 (venda B) + 6 (externa); a cancelada fica de fora.
        Assert.Equal(15m, daCategoria.QuantidadeItens);

        var top = await repositorio.ObterProdutosMaisVendidosComVendasAsync(inicio, fim, 1000);
        var doProduto = top.Single(t => t.CodigoInterno == $"{m}-01");
        Assert.Equal(15m, doProduto.QuantidadeVendida);
        // Venda A (uma, mesmo com duas linhas) + venda B + a externa.
        Assert.Equal(3, doProduto.QuantidadeVendas);
        // O item manual não tem produto: não entra num ranking de produtos.
        Assert.DoesNotContain(top, t => t.NomeProduto.Contains("Item manual"));

        await tx.RollbackAsync();
    }
}
