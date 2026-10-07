using ImperialColors.Domain.ReadModels;

namespace ImperialColors.Domain.Interfaces;

public interface IRelatorioAnalyticsRepository
{
    Task<IReadOnlyList<LinhaRelatorioVendaExternaResumo>> ObterLinhasVendasExternasPorPeriodoAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProdutoRankingResumo>> ObterProdutosMaisVendidosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProdutoRankingResumo>> ObterProdutosMenosVendidosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProdutoEncalhadoResumo>> ObterProdutosNuncaVendidosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    /// <summary>Itens vendidos no período, de balcão e de venda externa, já marcados com o
    /// canal de origem.</summary>
    Task<IReadOnlyList<LinhaVendaPorCanalResumo>> ObterVendasPorCanalAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    /// <summary>Extrato de entradas e saídas de estoque no período, por produto.</summary>
    Task<IReadOnlyList<LinhaMovimentacaoProdutoResumo>> ObterMovimentacoesProdutosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);
}
