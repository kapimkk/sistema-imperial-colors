using ImperialColors.Domain.Helpers;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Domain.ReadModels;

namespace ImperialColors.Application.Services;

public class DashboardService : IDashboardService
{
    private const decimal LimiteEstoqueCritico = 5m;
    private const int DiasLimiteValidadeProxima = 15;
    private const int TopEstoque = 10;
    private const int TopDestaques = 5;
    private static readonly DateTime InicioDoHistorico = new(2000, 1, 1);

    private readonly IVendaRepository _vendaRepository;
    private readonly IVendaExternaRepository _vendaExternaRepository;
    private readonly IVendaExternaService _vendaExternaService;
    private readonly IProdutoRepository _produtoRepository;
    private readonly IProdutoService _produtoService;
    private readonly IRelatorioAnalyticsService _relatorioAnalyticsService;

    public DashboardService(
        IVendaRepository vendaRepository,
        IVendaExternaRepository vendaExternaRepository,
        IVendaExternaService vendaExternaService,
        IProdutoRepository produtoRepository,
        IProdutoService produtoService,
        IRelatorioAnalyticsService relatorioAnalyticsService)
    {
        _vendaRepository = vendaRepository;
        _vendaExternaRepository = vendaExternaRepository;
        _vendaExternaService = vendaExternaService;
        _produtoRepository = produtoRepository;
        _produtoService = produtoService;
        _relatorioAnalyticsService = relatorioAnalyticsService;
    }

    /// <summary>
    /// Monta o dashboard a partir de UM resumo diário agregado pelo banco.
    ///
    /// Antes eram três chamadas a <c>ObterPorPeriodoAsync</c> (hoje, mês, últimos 7 dias),
    /// cada uma materializando o grafo <c>Venda → Itens → Produto</c> inteiro só para somar
    /// faturamento e custo — numa loja de 500 vendas/dia isso são ~112 mil linhas trazidas
    /// para a memória a cada abertura do app, e o dashboard abre no login.
    ///
    /// Agora: uma única consulta cobre o intervalo que engloba os três recortes (o menor
    /// entre o início do mês e o início dos últimos 7 dias — os dois divergem na primeira
    /// semana do mês, quando "últimos 7 dias" cruza para o mês anterior). O resultado tem no
    /// máximo ~37 linhas, uma por dia com movimento, e os três recortes saem de filtros
    /// triviais sobre essa lista.
    /// </summary>
    public async Task<DashboardDto> ObterDadosDashboardAsync()
    {
        var hoje = Relogio.Hoje;
        var (inicioMes, fimMesExclusivo) = ObterMesAtual(hoje);
        var inicioUltimos7Dias = hoje.AddDays(-6);
        var amanha = hoje.AddDays(1);

        var inicioCobertura = inicioMes < inicioUltimos7Dias ? inicioMes : inicioUltimos7Dias;

        // Balcão e rua entram no MESMO resumo: para o lojista o dinheiro que entrou no dia é
        // um só, e uma venda externa que não aparecesse aqui faria o faturamento do Dashboard
        // divergir do Relatório Consolidado de Vendas, que já soma as duas origens.
        var resumoBalcao = await _vendaRepository.ObterResumoDiarioAsync(inicioCobertura, amanha);
        var resumoExternas = await _vendaExternaRepository.ObterResumoDiarioAsync(inicioCobertura, amanha);
        var resumoDiario = SomarPorDia(resumoBalcao, resumoExternas);

        var totalProdutos = await _produtoRepository.ContarAsync();
        var produtosEstoqueCritico = await _produtoRepository.ContarComEstoqueCriticoAsync(LimiteEstoqueCritico);
        var produtosSemEstoque = await _produtoRepository.ContarSemEstoqueAsync();

        var diasDoMes = resumoDiario.Where(r => r.Data >= inicioMes && r.Data < fimMesExclusivo).ToList();
        var diaDeHoje = resumoDiario.FirstOrDefault(r => r.Data == hoje);

        var faturamentoHoje = diaDeHoje?.Faturamento ?? 0m;
        var custoHoje = diaDeHoje?.Custo ?? 0m;

        var faturamentoMes = diasDoMes.Sum(r => r.Faturamento);
        var custoMes = diasDoMes.Sum(r => r.Custo);
        var quantidadeVendasMes = diasDoMes.Sum(r => r.QuantidadeVendas);
        var itensSemCustoMes = diasDoMes.Sum(r => r.ItensSemCusto);

        // Os 7 dias saem sempre completos (dias sem venda entram zerados) para o gráfico
        // manter largura fixa — o resumo do banco só traz dias com movimento.
        var porDia = resumoDiario.ToDictionary(r => r.Data);
        var lucroUltimos7Dias = new List<LucroDiarioDto>();
        for (var i = 6; i >= 0; i--)
        {
            var dia = hoje.AddDays(-i);
            porDia.TryGetValue(dia, out var resumo);

            lucroUltimos7Dias.Add(new LucroDiarioDto
            {
                Data = dia,
                Faturamento = resumo?.Faturamento ?? 0m,
                Custo = resumo?.Custo ?? 0m,
                Lucro = resumo?.Lucro ?? 0m
            });
        }

        var maiorFaturamentoDoPeriodo = lucroUltimos7Dias.Count > 0 ? lucroUltimos7Dias.Max(d => d.Faturamento) : 0m;
        foreach (var dia in lucroUltimos7Dias)
            dia.PercentualBarra = maiorFaturamentoDoPeriodo > 0
                ? Math.Round(dia.Faturamento / maiorFaturamentoDoPeriodo * 100m, 1)
                : 0m;

        return new DashboardDto
        {
            // "Total de vendas" e "faturamento" saem agora da MESMA fonte. Antes vinham de
            // consultas distintas (ObterTotalVendasDiaAsync/MesAsync contra
            // ObterPorPeriodoAsync com fimMes = 23:59:59), que podiam divergir na virada.
            TotalVendasHoje = faturamentoHoje,
            TotalVendasMes = faturamentoMes,
            QuantidadeVendasHoje = diaDeHoje?.QuantidadeVendas ?? 0,
            ProdutosEstoqueCritico = produtosEstoqueCritico,
            ProdutosSemEstoque = produtosSemEstoque,
            TotalProdutos = totalProdutos,
            LucroHoje = faturamentoHoje - custoHoje,
            LucroMes = faturamentoMes - custoMes,
            CustoHoje = custoHoje,
            CustoMes = custoMes,
            MargemLucroHoje = faturamentoHoje > 0 ? Math.Round((faturamentoHoje - custoHoje) / faturamentoHoje * 100m, 1) : 0m,
            MargemLucroMes = faturamentoMes > 0 ? Math.Round((faturamentoMes - custoMes) / faturamentoMes * 100m, 1) : 0m,
            TicketMedioMes = quantidadeVendasMes > 0 ? Math.Round(faturamentoMes / quantidadeVendasMes, 2) : 0m,
            QuantidadeVendasMes = quantidadeVendasMes,
            ItensSemCustoCadastradoMes = itensSemCustoMes,
            LucroUltimos7Dias = lucroUltimos7Dias
        };
    }

    public async Task<DashboardEstoqueDto> ObterVisaoEstoqueAsync(CancellationToken cancellationToken = default)
    {
        var (inicioMes, fimMesExclusivo) = ObterMesAtual(Relogio.Hoje);

        // Reaproveita os serviços já existentes (Relatórios/Estoque) em vez de duplicar
        // consulta/mapeamento aqui — mesmo padrão que RelatorioAnalyticsService já usa
        // (injetar outro Application service, não só repositórios).
        var proximosValidade = await _produtoService.ObterProximosDaValidadeAsync(DiasLimiteValidadeProxima);
        var poucaQuantidade = await _produtoService.ObterComEstoqueBaixoAsync();
        // ObterRankingProdutosAsync tem limite INCLUSIVO (usa <= fim); AddTicks(-1) converte o
        // limite meio-aberto para o último instante do mês, sem perder o segundo final.
        var maisVendidos = await _relatorioAnalyticsService.ObterRankingProdutosAsync(
            inicioMes, fimMesExclusivo.AddTicks(-1), TipoAnaliseGiroProduto.MaisVendidos, cancellationToken);

        return new DashboardEstoqueDto
        {
            ProximosDaValidade = proximosValidade.OrderBy(p => p.DataValidade).Take(TopEstoque).ToList(),
            PoucaQuantidade = poucaQuantidade.OrderBy(p => p.QuantidadeEstoque).Take(TopEstoque).ToList(),
            MaisVendidos = maisVendidos.Take(TopDestaques).ToList()
        };
    }

    public async Task<DashboardVendasDto> ObterVisaoVendasAsync(CancellationToken cancellationToken = default)
    {
        var (inicioMes, fimMesExclusivo) = ObterMesAtual(Relogio.Hoje);
        // ObterPorPeriodoAsync também é inclusivo — ver comentário em ObterVisaoEstoqueAsync.
        var vendasMes = await _vendaRepository.ObterPorPeriodoAsync(inicioMes, fimMesExclusivo.AddTicks(-1));

        var externasMes = await _vendaExternaRepository.ObterPorPeriodoAsync(inicioMes, fimMesExclusivo.AddTicks(-1), cancellationToken);

        var destaquesBalcao = vendasMes.Select(v => new VendaDestaqueDto
        {
            Data = v.DataVenda,
            ClienteNome = v.Cliente?.Nome ?? v.NomeCompradorCupom ?? "Consumidor",
            Total = v.Total,
            FormaPagamentoDescricao = PagamentoHelper.ObterDescricao(v.FormaPagamento, v.QuantidadeParcelas)
        });

        // A venda de rua disputa a lista em pé de igualdade: com o faturamento do painel já
        // somando as duas origens, uma venda externa grande de fora da lista deixaria o
        // destaque incoerente com o card logo acima. Ela não tem cliente nem forma de
        // pagamento no modelo, então o número identifica a venda e o pagamento fica em
        // branco — em vez de inventar um valor que ninguém registrou.
        var destaquesExternas = externasMes.Select(v => new VendaDestaqueDto
        {
            Data = v.DataVenda,
            ClienteNome = $"Venda externa {v.NumeroVendaExterna}",
            Total = v.Total,
            FormaPagamentoDescricao = "—"
        });

        var maioresVendas = destaquesBalcao
            .Concat(destaquesExternas)
            .OrderByDescending(v => v.Total)
            .Take(TopDestaques)
            .ToList();

        return new DashboardVendasDto { MaioresVendas = maioresVendas };
    }

    /// <summary>
    /// Itens por categoria e top 5 de produtos no mês corrente ou no histórico inteiro.
    ///
    /// "Total" parte de uma data fixa e antiga em vez de <c>DateTime.MinValue</c>: o Npgsql
    /// trata MinValue como "-infinity" ao gravar/comparar timestamps, e um limite que o
    /// provider reinterpreta é o tipo de detalhe que funciona até a versão em que deixa de
    /// funcionar. O fim é o último instante de hoje — venda não nasce no futuro.
    /// </summary>
    public async Task<DashboardProdutosVendidosDto> ObterProdutosVendidosAsync(
        PeriodoDashboard periodo, CancellationToken cancellationToken = default)
    {
        var hoje = Relogio.Hoje;
        var (inicioMes, fimMesExclusivo) = ObterMesAtual(hoje);

        var (inicio, fim) = periodo == PeriodoDashboard.Total
            ? (InicioDoHistorico, hoje.AddDays(1).AddTicks(-1))
            : (inicioMes, fimMesExclusivo.AddTicks(-1));

        var itensPorCategoria = await _relatorioAnalyticsService.ObterItensVendidosPorCategoriaAsync(
            inicio, fim, cancellationToken);
        var produtosMaisVendidos = await _relatorioAnalyticsService.ObterProdutosMaisVendidosComVendasAsync(
            inicio, fim, TopDestaques, cancellationToken);

        return new DashboardProdutosVendidosDto
        {
            Periodo = periodo,
            ItensPorCategoria = itensPorCategoria.ToList(),
            ProdutosMaisVendidos = produtosMaisVendidos.ToList()
        };
    }

    /// <summary>
    /// Reaproveita o resumo do próprio módulo de vendas externas em vez de refazer a
    /// consulta aqui — mesmo padrão já usado na visão de Estoque, que chama o serviço de
    /// produtos em vez de duplicar o mapeamento.
    /// </summary>
    public Task<ResumoComissoesDto> ObterVisaoComissoesAsync(CancellationToken cancellationToken = default)
        => _vendaExternaService.ObterResumoComissoesAsync(cancellationToken);

    /// <summary>
    /// Junta os resumos diários de balcão e de venda externa numa lista só, somando dia a
    /// dia. Um dia que só teve venda de rua aparece igual — é uma linha nova, não um dia
    /// perdido.
    /// </summary>
    private static IReadOnlyList<ResumoVendasDiario> SomarPorDia(
        IReadOnlyList<ResumoVendasDiario> balcao, IReadOnlyList<ResumoVendasDiario> externas)
    {
        if (externas.Count == 0)
            return balcao;

        return balcao.Concat(externas)
            .GroupBy(r => r.Data)
            .Select(g => new ResumoVendasDiario
            {
                Data = g.Key,
                QuantidadeVendas = g.Sum(r => r.QuantidadeVendas),
                Faturamento = g.Sum(r => r.Faturamento),
                Custo = g.Sum(r => r.Custo),
                ItensSemCusto = g.Sum(r => r.ItensSemCusto)
            })
            .OrderBy(r => r.Data)
            .ToList();
    }

    /// <summary>
    /// Mês corrente como intervalo MEIO-ABERTO <c>[inicio, fimExclusivo)</c>. Antes o fim era
    /// <c>AddMonths(1).AddSeconds(-1)</c>, que descartava vendas no último segundo do mês e
    /// divergia de <c>ObterTotalVendasMesAsync</c> (que usava o mês-calendário inteiro): o
    /// card "Total do mês" e a soma do lucro podiam mostrar valores diferentes.
    /// </summary>
    private static (DateTime Inicio, DateTime FimExclusivo) ObterMesAtual(DateTime hoje)
    {
        var inicioMes = new DateTime(hoje.Year, hoje.Month, 1);
        return (inicioMes, inicioMes.AddMonths(1));
    }
}
