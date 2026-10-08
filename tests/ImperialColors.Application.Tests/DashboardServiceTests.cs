using System.Linq.Expressions;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Domain.ReadModels;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Cobre o cálculo de lucro/custo/margem do painel financeiro do dashboard
/// (<see cref="DashboardService"/>) — não depende de banco: <see cref="IVendaRepository"/>
/// é simulado a partir de uma lista de <see cref="Venda"/> em memória.
///
/// O mock de <c>ObterResumoDiarioAsync</c> reproduz, em LINQ, exatamente a agregação que a
/// implementação real faz em SQL (agrupar por dia, somar Total, somar Custo×Quantidade,
/// contar itens sem custo). Os casos de teste continuam expressos em vendas e itens — que é
/// como se raciocina sobre a regra — e o mock faz a ponte para o formato agregado que o
/// serviço passou a consumir.
/// </summary>
public class DashboardServiceTests
{
    private static Venda CriarVenda(DateTime dataVenda, decimal total, params (decimal? custo, decimal quantidade)[] itens)
    {
        var venda = new Venda { DataVenda = dataVenda, Total = total };
        foreach (var (custo, quantidade) in itens)
        {
            venda.Itens.Add(new ItemVenda
            {
                Quantidade = quantidade,
                Produto = new Produto { Nome = "Produto teste", Custo = custo }
            });
        }
        return venda;
    }

    private static (Mock<IVendaRepository> Venda, Mock<IProdutoRepository> Produto, Mock<IProdutoService> ProdutoService, Mock<IRelatorioAnalyticsService> Analytics) CriarMocks(
        List<Venda> todasAsVendas, decimal totalVendasHoje = 0, decimal totalVendasMes = 0)
    {
        var vendaMock = new Mock<IVendaRepository>();
        vendaMock.Setup(r => r.ObterTotalVendasDiaAsync(It.IsAny<DateTime>())).ReturnsAsync(totalVendasHoje);
        vendaMock.Setup(r => r.ObterTotalVendasMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(totalVendasMes);
        vendaMock.Setup(r => r.ObterPorPeriodoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .Returns((DateTime inicio, DateTime fim) => Task.FromResult<IEnumerable<Venda>>(
                todasAsVendas.Where(v => v.DataVenda >= inicio && v.DataVenda <= fim).ToList()));

        // Espelha a agregação SQL de VendaRepository.ObterResumoDiarioAsync. Intervalo
        // MEIO-ABERTO [inicio, fimExclusivo), igual ao real.
        vendaMock.Setup(r => r.ObterResumoDiarioAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns((DateTime inicio, DateTime fimExclusivo, CancellationToken _) =>
                Task.FromResult<IReadOnlyList<ResumoVendasDiario>>(
                    todasAsVendas
                        .Where(v => v.DataVenda >= inicio && v.DataVenda < fimExclusivo)
                        .GroupBy(v => v.DataVenda.Date)
                        .Select(g => new ResumoVendasDiario
                        {
                            Data = g.Key,
                            QuantidadeVendas = g.Count(),
                            Faturamento = g.Sum(v => v.Total),
                            Custo = g.SelectMany(v => v.Itens)
                                     .Sum(i => i.Produto?.Custo is { } c ? c * i.Quantidade : 0m),
                            ItensSemCusto = g.SelectMany(v => v.Itens)
                                             .Count(i => i.Produto?.Custo is null)
                        })
                        .OrderBy(r => r.Data)
                        .ToList()));

        var produtoMock = new Mock<IProdutoRepository>();
        produtoMock.Setup(p => p.ContarAsync(It.IsAny<Expression<Func<Produto, bool>>>())).ReturnsAsync(0);
        produtoMock.Setup(p => p.ContarComEstoqueCriticoAsync(It.IsAny<decimal>())).ReturnsAsync(0);
        produtoMock.Setup(p => p.ContarSemEstoqueAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var produtoServiceMock = new Mock<IProdutoService>();
        produtoServiceMock.Setup(p => p.ObterProximosDaValidadeAsync(It.IsAny<int>())).ReturnsAsync(new List<ProdutoDto>());
        produtoServiceMock.Setup(p => p.ObterComEstoqueBaixoAsync()).ReturnsAsync(new List<ProdutoDto>());

        var analyticsMock = new Mock<IRelatorioAnalyticsService>();
        analyticsMock
            .Setup(a => a.ObterRankingProdutosAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<TipoAnaliseGiroProduto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProdutoRankingDto>());
        // A visão de vendas também pede itens por categoria e o top 5 com nº de vendas.
        analyticsMock
            .Setup(a => a.ObterItensVendidosPorCategoriaAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CategoriaItensVendidosDto>());
        analyticsMock
            .Setup(a => a.ObterProdutosMaisVendidosComVendasAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProdutoMaisVendidoDto>());

        return (vendaMock, produtoMock, produtoServiceMock, analyticsMock);
    }

    /// <summary>Venda externa entra no mesmo formato agregado do balcão. Sem
    /// <paramref name="diasDeVendaExterna"/>, a loja não teve venda de rua no período.</summary>
    private static Mock<IVendaExternaRepository> CriarMockVendaExterna(params ResumoVendasDiario[] diasDeVendaExterna)
    {
        var mock = new Mock<IVendaExternaRepository>();
        mock.Setup(r => r.ObterResumoDiarioAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns((DateTime inicio, DateTime fimExclusivo, CancellationToken _) =>
                Task.FromResult<IReadOnlyList<ResumoVendasDiario>>(
                    diasDeVendaExterna.Where(d => d.Data >= inicio && d.Data < fimExclusivo).ToList()));
        mock.Setup(r => r.ObterPorPeriodoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VendaExterna>());
        return mock;
    }

    /// <summary>Mock da lista de vendas externas do mês — usado pelos destaques
    /// ("Maiores Vendas do Mês"), que trabalham com a venda inteira, não com o resumo.</summary>
    private static Mock<IVendaExternaRepository> CriarMockVendaExternaComVendas(params VendaExterna[] vendasExternas)
    {
        var mock = CriarMockVendaExterna();
        mock.Setup(r => r.ObterPorPeriodoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns((DateTime inicio, DateTime fim, CancellationToken _) =>
                Task.FromResult<IEnumerable<VendaExterna>>(
                    vendasExternas.Where(v => v.DataVenda >= inicio && v.DataVenda <= fim).ToList()));
        return mock;
    }

    private static DashboardService CriarServico(
        Mock<IVendaRepository> venda, Mock<IProdutoRepository> produto, Mock<IProdutoService> produtoService, Mock<IRelatorioAnalyticsService> analytics,
        Mock<IVendaExternaRepository>? vendaExterna = null)
        => new(venda.Object, (vendaExterna ?? CriarMockVendaExterna()).Object, Mock.Of<IVendaExternaService>(),
            produto.Object, produtoService.Object, analytics.Object);

    [Fact]
    public async Task ObterDadosDashboardAsync_CalculaLucroECustoAPartirDoCustoDoProduto()
    {
        var hoje = DateTime.Today;
        var vendas = new List<Venda>
        {
            // 1 venda hoje: R$100 de faturamento, custo unitário R$60 × 1 = R$60 → lucro R$40 (margem 40%).
            CriarVenda(hoje.AddHours(10), 100m, (custo: 60m, quantidade: 1m))
        };
        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(vendas);
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        var dados = await service.ObterDadosDashboardAsync();

        Assert.Equal(40m, dados.LucroHoje);
        Assert.Equal(60m, dados.CustoHoje);
        Assert.Equal(40m, dados.MargemLucroHoje);
        Assert.Equal(0, dados.ItensSemCustoCadastradoMes);
    }

    [Fact]
    public async Task ObterDadosDashboardAsync_ItemSemCustoCadastrado_NaoEntraNoCustoMasEhContado()
    {
        var hoje = DateTime.Today;
        var vendas = new List<Venda>
        {
            // Produto sem custo cadastrado (null) — entra no faturamento, não no custo.
            CriarVenda(hoje.AddHours(9), 50m, (custo: null, quantidade: 2m))
        };
        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(vendas);
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        var dados = await service.ObterDadosDashboardAsync();

        Assert.Equal(0m, dados.CustoMes);
        Assert.Equal(50m, dados.LucroMes); // sem custo conhecido, lucro = faturamento inteiro
        Assert.Equal(1, dados.ItensSemCustoCadastradoMes);
    }

    [Fact]
    public async Task ObterDadosDashboardAsync_CustoMaiorQueFaturamento_GeraLucroEMargemNegativos()
    {
        var hoje = DateTime.Today;
        var vendas = new List<Venda>
        {
            // Vendido por R$30 um item que custou R$50 (promoção/erro de preço) → prejuízo.
            CriarVenda(hoje.AddHours(11), 30m, (custo: 50m, quantidade: 1m))
        };
        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(vendas);
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        var dados = await service.ObterDadosDashboardAsync();

        Assert.Equal(-20m, dados.LucroMes);
        Assert.True(dados.MargemLucroMes < 0);
    }

    [Fact]
    public async Task ObterDadosDashboardAsync_SemVendasNoPeriodo_MargemZeroSemDivisaoPorZero()
    {
        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda>());
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        var dados = await service.ObterDadosDashboardAsync();

        Assert.Equal(0m, dados.LucroHoje);
        Assert.Equal(0m, dados.MargemLucroHoje);
        Assert.Equal(0m, dados.LucroMes);
        Assert.Equal(0m, dados.MargemLucroMes);
        Assert.Equal(0m, dados.TicketMedioMes);
    }

    [Fact]
    public async Task ObterDadosDashboardAsync_LucroUltimos7Dias_TemExatamenteSeteDiasNaOrdemCorreta()
    {
        var hoje = DateTime.Today;
        var vendas = new List<Venda> { CriarVenda(hoje, 100m, (custo: 40m, quantidade: 1m)) };
        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(vendas);
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        var dados = await service.ObterDadosDashboardAsync();

        Assert.Equal(7, dados.LucroUltimos7Dias.Count);
        Assert.Equal(hoje.AddDays(-6), dados.LucroUltimos7Dias.First().Data);
        Assert.Equal(hoje, dados.LucroUltimos7Dias.Last().Data);

        // O dia com maior faturamento do período (hoje, único com venda) deve ficar com a
        // barra no máximo (100%) — é o que normaliza a largura visual no dashboard.
        var diaDeHoje = dados.LucroUltimos7Dias.Last();
        Assert.Equal(100m, diaDeHoje.PercentualBarra);
        Assert.Equal(60m, diaDeHoje.Lucro);
    }

    [Fact]
    public async Task ObterDadosDashboardAsync_TicketMedioMes_DivideFaturamentoPelaQuantidadeDeVendas()
    {
        var hoje = DateTime.Today;
        var vendas = new List<Venda>
        {
            CriarVenda(hoje, 100m, (custo: 40m, quantidade: 1m)),
            CriarVenda(hoje, 200m, (custo: 80m, quantidade: 1m))
        };
        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(vendas);
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        var dados = await service.ObterDadosDashboardAsync();

        Assert.Equal(2, dados.QuantidadeVendasMes);
        Assert.Equal(150m, dados.TicketMedioMes); // (100 + 200) / 2
    }

    // ===== Visão Estoque =====

    [Fact]
    public async Task ObterVisaoEstoqueAsync_LimitaListasAoTopEsperado()
    {
        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda>());

        var produtosValidade = Enumerable.Range(1, 15)
            .Select(i => new ProdutoDto { Id = i, Nome = $"Produto {i}", DataValidade = DateTime.Today.AddDays(i) })
            .ToList();
        var produtosBaixoEstoque = Enumerable.Range(1, 15)
            .Select(i => new ProdutoDto { Id = i, Nome = $"Produto {i}", QuantidadeEstoque = i })
            .ToList();
        var ranking = Enumerable.Range(1, 8)
            .Select(i => new ProdutoRankingDto { Posicao = i, NomeProduto = $"Produto {i}" })
            .ToList();

        produtoServiceMock.Setup(p => p.ObterProximosDaValidadeAsync(It.IsAny<int>())).ReturnsAsync(produtosValidade);
        produtoServiceMock.Setup(p => p.ObterComEstoqueBaixoAsync()).ReturnsAsync(produtosBaixoEstoque);
        analyticsMock
            .Setup(a => a.ObterRankingProdutosAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<TipoAnaliseGiroProduto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ranking);

        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);
        var dados = await service.ObterVisaoEstoqueAsync();

        Assert.Equal(10, dados.ProximosDaValidade.Count);
        Assert.Equal(10, dados.PoucaQuantidade.Count);
        Assert.Equal(5, dados.MaisVendidos.Count);
    }

    [Fact]
    public async Task ObterVisaoEstoqueAsync_OrdenaProximosDaValidadePorDataCrescente()
    {
        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda>());
        var produtos = new List<ProdutoDto>
        {
            new() { Id = 1, Nome = "Vence em 10 dias", DataValidade = DateTime.Today.AddDays(10) },
            new() { Id = 2, Nome = "Vence amanhã", DataValidade = DateTime.Today.AddDays(1) },
            new() { Id = 3, Nome = "Já vencido", DataValidade = DateTime.Today.AddDays(-2) }
        };
        produtoServiceMock.Setup(p => p.ObterProximosDaValidadeAsync(It.IsAny<int>())).ReturnsAsync(produtos);

        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);
        var dados = await service.ObterVisaoEstoqueAsync();

        Assert.Equal("Já vencido", dados.ProximosDaValidade[0].Nome);
        Assert.Equal("Vence amanhã", dados.ProximosDaValidade[1].Nome);
        Assert.Equal("Vence em 10 dias", dados.ProximosDaValidade[2].Nome);
    }

    [Fact]
    public async Task ObterVisaoEstoqueAsync_PassaPeriodoDoMesAtualEMaisVendidosParaORanking()
    {
        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda>());
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        await service.ObterVisaoEstoqueAsync();

        var hoje = DateTime.Today;
        var inicioMesEsperado = new DateTime(hoje.Year, hoje.Month, 1);
        // O fim passado ao ranking agora é o ÚLTIMO INSTANTE do mês (AddTicks(-1) sobre o
        // limite meio-aberto), não 23:59:59 — antes o último segundo do mês era descartado.
        var fimMesEsperado = inicioMesEsperado.AddMonths(1).AddTicks(-1);

        analyticsMock.Verify(a => a.ObterRankingProdutosAsync(
            inicioMesEsperado, fimMesEsperado, TipoAnaliseGiroProduto.MaisVendidos, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===== Visão Vendas =====

    [Fact]
    public async Task ObterVisaoVendasAsync_OrdenaPorTotalDecrescenteELimitaATop5()
    {
        var hoje = DateTime.Today;
        var vendas = Enumerable.Range(1, 8)
            .Select(i => CriarVenda(hoje, i * 10m))
            .ToList();
        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(vendas);
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        var dados = await service.ObterVisaoVendasAsync();

        Assert.Equal(5, dados.MaioresVendas.Count);
        Assert.Equal(80m, dados.MaioresVendas[0].Total);
        Assert.Equal(40m, dados.MaioresVendas[4].Total);
    }

    [Theory]
    [InlineData(true, "Cliente Cadastrado", null, "Cliente Cadastrado")]
    [InlineData(false, null, "Nome do Cupom", "Nome do Cupom")]
    [InlineData(false, null, null, "Consumidor")]
    public async Task ObterVisaoVendasAsync_ClienteNome_UsaFallbackCorreto(
        bool temCliente, string? nomeCliente, string? nomeCompradorCupom, string esperado)
    {
        var venda = CriarVenda(DateTime.Today, 100m);
        venda.NomeCompradorCupom = nomeCompradorCupom;
        if (temCliente) venda.Cliente = new Cliente { Nome = nomeCliente! };

        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda> { venda });
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        var dados = await service.ObterVisaoVendasAsync();

        Assert.Equal(esperado, dados.MaioresVendas[0].ClienteNome);
    }

    [Fact]
    public async Task ObterVisaoVendasAsync_FormaPagamentoDescricao_UsaPagamentoHelper()
    {
        var venda = CriarVenda(DateTime.Today, 118m);
        venda.FormaPagamento = ImperialColors.Domain.Enums.FormaPagamento.CartaoCredito;
        venda.QuantidadeParcelas = 3;

        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda> { venda });
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        var dados = await service.ObterVisaoVendasAsync();

        var esperado = ImperialColors.Application.Helpers.PagamentoHelper.ObterDescricao(
            ImperialColors.Domain.Enums.FormaPagamento.CartaoCredito, 3);
        Assert.Equal(esperado, dados.MaioresVendas[0].FormaPagamentoDescricao);
    }

    // ===== Venda externa no painel financeiro =====
    // Para o lojista o dinheiro que entrou no dia é um só. Antes, a venda de rua não entrava
    // em nada do Dashboard, e o faturamento exibido divergia do Relatório Consolidado de
    // Vendas, que sempre somou as duas origens.

    [Fact]
    public async Task ObterDadosDashboardAsync_SomaFaturamentoCustoEQuantidadeDaVendaExterna()
    {
        var hoje = DateTime.Today;
        // Balcão: R$ 100 de faturamento, R$ 60 de custo.
        var balcao = CriarVenda(hoje.AddHours(10), 100m, (custo: 60m, quantidade: 1m));
        // Rua no mesmo dia: R$ 300 de faturamento, R$ 180 de custo.
        var externa = new ResumoVendasDiario
        {
            Data = hoje,
            QuantidadeVendas = 1,
            Faturamento = 300m,
            Custo = 180m
        };

        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda> { balcao });
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock, CriarMockVendaExterna(externa));

        var dados = await service.ObterDadosDashboardAsync();

        Assert.Equal(400m, dados.TotalVendasHoje);
        Assert.Equal(400m, dados.TotalVendasMes);
        Assert.Equal(240m, dados.CustoHoje);
        Assert.Equal(160m, dados.LucroHoje);   // 400 − 240
        Assert.Equal(2, dados.QuantidadeVendasHoje);
        Assert.Equal(2, dados.QuantidadeVendasMes);
        Assert.Equal(200m, dados.TicketMedioMes); // 400 / 2 vendas
    }

    /// <summary>Dia que teve SÓ venda de rua tem que aparecer — é uma linha nova no resumo,
    /// não um dia sem movimento.</summary>
    [Fact]
    public async Task ObterDadosDashboardAsync_DiaSoComVendaExterna_ApareceNoFaturamento()
    {
        var hoje = DateTime.Today;
        var externa = new ResumoVendasDiario { Data = hoje, QuantidadeVendas = 2, Faturamento = 250m, Custo = 100m };

        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda>());
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock, CriarMockVendaExterna(externa));

        var dados = await service.ObterDadosDashboardAsync();

        Assert.Equal(250m, dados.TotalVendasHoje);
        Assert.Equal(150m, dados.LucroHoje);
        Assert.Equal(2, dados.QuantidadeVendasHoje);
        Assert.Equal(60m, dados.MargemLucroHoje); // 150 / 250
    }

    /// <summary>Item avulso da rua (digitado sem produto do estoque) não tem custo — o aviso
    /// de lucro subestimado precisa contá-lo junto com os do balcão.</summary>
    [Fact]
    public async Task ObterDadosDashboardAsync_ItensSemCustoDaRua_SomamComOsDoBalcao()
    {
        var hoje = DateTime.Today;
        var balcao = CriarVenda(hoje.AddHours(9), 50m, (custo: null, quantidade: 1m));
        var externa = new ResumoVendasDiario { Data = hoje, QuantidadeVendas = 1, Faturamento = 80m, Custo = 0m, ItensSemCusto = 2 };

        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda> { balcao });
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock, CriarMockVendaExterna(externa));

        var dados = await service.ObterDadosDashboardAsync();

        Assert.Equal(3, dados.ItensSemCustoCadastradoMes);
    }

    /// <summary>O gráfico dos últimos 7 dias sai do mesmo resumo — se a rua não entrasse
    /// nele, a barra do dia mostraria menos do que o card de faturamento do mesmo dia.</summary>
    [Fact]
    public async Task ObterDadosDashboardAsync_GraficoDosUltimos7Dias_IncluiAVendaExterna()
    {
        var hoje = DateTime.Today;
        var ontem = hoje.AddDays(-1);
        var balcao = CriarVenda(ontem.AddHours(15), 100m, (custo: 40m, quantidade: 1m));
        var externa = new ResumoVendasDiario { Data = ontem, QuantidadeVendas = 1, Faturamento = 200m, Custo = 50m };

        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda> { balcao });
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock, CriarMockVendaExterna(externa));

        var dados = await service.ObterDadosDashboardAsync();

        var diaDeOntem = dados.LucroUltimos7Dias.Single(d => d.Data == ontem);
        Assert.Equal(300m, diaDeOntem.Faturamento);
        Assert.Equal(90m, diaDeOntem.Custo);
        Assert.Equal(210m, diaDeOntem.Lucro);
    }

    /// <summary>
    /// "Maiores Vendas do Mês" disputa entre as duas origens: com o card de faturamento logo
    /// acima já somando a rua, uma venda externa maior que todas as de balcão não podia ficar
    /// de fora da lista.
    /// </summary>
    [Fact]
    public async Task ObterVisaoVendasAsync_VendaExternaMaiorQueAsDeBalcao_LideraOsDestaques()
    {
        var hoje = DateTime.Today;
        var balcao = CriarVenda(hoje.AddHours(10), 100m);
        var externa = new VendaExterna
        {
            NumeroVendaExterna = "VE-0007",
            DataVenda = hoje.AddHours(11),
            Total = 900m
        };

        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda> { balcao });
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock, CriarMockVendaExternaComVendas(externa));

        var dados = await service.ObterVisaoVendasAsync();

        Assert.Equal(2, dados.MaioresVendas.Count);
        Assert.Equal(900m, dados.MaioresVendas[0].Total);
        Assert.Equal("Venda externa VE-0007", dados.MaioresVendas[0].ClienteNome);
        // Venda externa não registra forma de pagamento — a coluna fica em branco em vez de
        // exibir um valor que ninguém informou.
        Assert.Equal("—", dados.MaioresVendas[0].FormaPagamentoDescricao);
    }

    /// <summary>Sem venda de rua no período, o resultado é o mesmo de antes da mudança.</summary>
    [Fact]
    public async Task ObterDadosDashboardAsync_SemVendaExterna_NaoAlteraOsNumerosDoBalcao()
    {
        var hoje = DateTime.Today;
        var balcao = CriarVenda(hoje.AddHours(10), 100m, (custo: 60m, quantidade: 1m));

        var (vendaMock, produtoMock, produtoServiceMock, analyticsMock) = CriarMocks(new List<Venda> { balcao });
        var service = CriarServico(vendaMock, produtoMock, produtoServiceMock, analyticsMock);

        var dados = await service.ObterDadosDashboardAsync();

        Assert.Equal(100m, dados.TotalVendasHoje);
        Assert.Equal(40m, dados.LucroHoje);
        Assert.Equal(1, dados.QuantidadeVendasHoje);
    }
}
