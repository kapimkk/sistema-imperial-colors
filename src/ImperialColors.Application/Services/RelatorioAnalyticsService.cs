using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Helpers;
using ImperialColors.Domain.Interfaces;

namespace ImperialColors.Application.Services;

public class RelatorioAnalyticsService : IRelatorioAnalyticsService
{
    private readonly IRelatorioAnalyticsRepository _repository;
    private readonly IVendaService _vendaService;
    private readonly IVendaExternaService _vendaExternaService;

    public RelatorioAnalyticsService(
        IRelatorioAnalyticsRepository repository,
        IVendaService vendaService,
        IVendaExternaService vendaExternaService)
    {
        _repository = repository;
        _vendaService = vendaService;
        _vendaExternaService = vendaExternaService;
    }

    public async Task<IReadOnlyList<LinhaRelatorioVendaExternaDto>> ObterLinhasVendasExternasAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        var linhas = await _repository.ObterLinhasVendasExternasPorPeriodoAsync(inicio, fim, cancellationToken);
        return linhas.Select(l => new LinhaRelatorioVendaExternaDto
        {
            DataVenda = l.DataVenda,
            CodigoVenda = l.CodigoVenda,
            ProdutoItem = l.ProdutoItem,
            QuantidadeVendida = l.QuantidadeVendida,
            ValorUnitario = l.ValorUnitario,
            ValorTotal = l.ValorTotal
        }).ToList();
    }

    public async Task<IReadOnlyList<ProdutoRankingDto>> ObterRankingProdutosAsync(
        DateTime inicio, DateTime fim, TipoAnaliseGiroProduto tipo, CancellationToken cancellationToken = default)
    {
        var ranking = tipo switch
        {
            TipoAnaliseGiroProduto.MaisVendidos =>
                await _repository.ObterProdutosMaisVendidosAsync(inicio, fim, cancellationToken),
            TipoAnaliseGiroProduto.MenosVendidos =>
                await _repository.ObterProdutosMenosVendidosAsync(inicio, fim, cancellationToken),
            _ => Array.Empty<Domain.ReadModels.ProdutoRankingResumo>()
        };

        return ranking
            .Select((r, index) => new ProdutoRankingDto
            {
                Posicao = index + 1,
                CodigoInterno = r.CodigoInterno,
                NomeProduto = r.NomeProduto,
                QuantidadeTotal = r.QuantidadeTotal,
                FaturamentoGerado = r.FaturamentoGerado
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ProdutoEncalhadoDto>> ObterProdutosEncalhadosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        var itens = await _repository.ObterProdutosNuncaVendidosAsync(inicio, fim, cancellationToken);
        return itens.Select(i => new ProdutoEncalhadoDto
        {
            CodigoInterno = i.CodigoInterno,
            NomeProduto = i.NomeProduto,
            EstoqueAtual = i.EstoqueAtual,
            ValorTotalParado = i.ValorTotalParado
        }).ToList();
    }

    public async Task<IReadOnlyList<LinhaRelatorioVendaConsolidadaDto>> ObterVendasConsolidadasAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        var vendasBalcao = await _vendaService.ObterPorPeriodoAsync(inicio, fim);
        // Filtra no banco (não carrega o histórico inteiro de vendas externas em memória
        // a cada relatório) — a mesma query já usada pela listagem paginada de vendas.
        var vendasExternas = await _vendaExternaService.ObterPorPeriodoAsync(inicio, fim, cancellationToken);

        var linhas = new List<LinhaRelatorioVendaConsolidadaDto>();

        foreach (var venda in vendasBalcao)
        {
            linhas.Add(new LinhaRelatorioVendaConsolidadaDto
            {
                DataVenda = venda.DataVenda,
                Origem = "Balcão",
                NumeroVenda = venda.NumeroVenda,
                ClienteOuResumo = venda.ClienteNome ?? venda.NomeCompradorExibicao,
                TotalItens = venda.Itens?.Count ?? 0,
                Subtotal = venda.Subtotal,
                Desconto = venda.Desconto,
                Total = venda.Total,
                FormaPagamento = venda.FormaPagamentoDescricao
            });
        }

        foreach (var venda in vendasExternas)
        {
            linhas.Add(new LinhaRelatorioVendaConsolidadaDto
            {
                DataVenda = venda.DataVenda,
                Origem = "Externa",
                NumeroVenda = venda.NumeroVendaExterna,
                ClienteOuResumo = string.IsNullOrWhiteSpace(venda.Observacoes) ? "Venda de rua" : venda.Observacoes!,
                TotalItens = venda.TotalItens,
                Subtotal = venda.Subtotal,
                Desconto = 0,
                Comissao = venda.Comissao,
                // Líquido, como no Dashboard — ver VendaExterna.TotalLiquido.
                Total = venda.TotalLiquido,
                FormaPagamento = "Venda Externa"
            });
        }

        return linhas
            .OrderByDescending(l => l.DataVenda)
            .ThenBy(l => l.NumeroVenda)
            .ToList();
    }

    public async Task<IReadOnlyList<LinhaVendaPorCanalDto>> ObterVendasPorCanalAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        var linhas = await _repository.ObterVendasPorCanalAsync(inicio, fim, cancellationToken);
        return linhas.Select(l => new LinhaVendaPorCanalDto
        {
            DataVenda = l.DataVenda,
            Canal = l.Canal,
            CodigoProduto = l.CodigoProduto,
            NomeProduto = l.NomeProduto,
            NumeroVenda = l.NumeroVenda,
            Quantidade = l.Quantidade,
            ValorUnitario = l.ValorUnitario,
            ValorTotal = l.ValorTotal,
            ProdutoCadastrado = l.ProdutoCadastrado
        }).ToList();
    }

    public async Task<IReadOnlyList<LinhaMovimentacaoProdutoDto>> ObterMovimentacoesProdutosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default)
    {
        var linhas = await _repository.ObterMovimentacoesProdutosAsync(inicio, fim, cancellationToken);
        return linhas.Select(l => new LinhaMovimentacaoProdutoDto
        {
            Data = l.Data,
            CodigoProduto = l.CodigoProduto,
            NomeProduto = l.NomeProduto,
            Unidade = l.Unidade,
            Tipo = l.Tipo,
            Quantidade = l.Quantidade,
            SaldoAnterior = l.SaldoAnterior,
            SaldoPosterior = l.SaldoPosterior,
            Motivo = l.Motivo,
            Usuario = l.Usuario,
            NumeroVenda = l.NumeroVenda,
            Canal = l.Canal
        }).ToList();
    }

    public IReadOnlyList<TotalCanalDto> TotalizarPorCanal(IEnumerable<LinhaVendaPorCanalDto> linhas)
    {
        var porCanal = linhas
            .GroupBy(l => l.Canal)
            .ToDictionary(g => g.Key, g => new TotalCanalDto
            {
                Canal = g.Key,
                QuantidadeLinhas = g.Count(),
                QuantidadeItens = g.Sum(l => l.Quantidade),
                ValorTotal = g.Sum(l => l.ValorTotal)
            });

        // Percorre a ordem fixa, e não o que apareceu no período: assim o rodapé tem sempre
        // as mesmas linhas na mesma sequência, e um canal zerado aparece zerado em vez de
        // sumir — o lojista precisa enxergar que o site não vendeu, não deduzir pela ausência.
        return CanalVendaHelper.OrdemRelatorio
            .Select(canal => porCanal.TryGetValue(canal, out var total)
                ? total
                : new TotalCanalDto { Canal = canal })
            .ToList();
    }
}
