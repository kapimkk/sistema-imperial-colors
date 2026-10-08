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

    /// <summary>Total de itens vendidos por categoria (balcão e venda externa).</summary>
    Task<IReadOnlyList<CategoriaItensVendidosResumo>> ObterItensVendidosPorCategoriaAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    /// <summary>Os produtos mais vendidos do período, cada um com a quantidade de vendas em que
    /// apareceu.</summary>
    Task<IReadOnlyList<ProdutoMaisVendidoResumo>> ObterProdutosMaisVendidosComVendasAsync(
        DateTime inicio, DateTime fim, int quantidade, CancellationToken cancellationToken = default);

    /// <summary>Extrato de entradas e saídas de estoque no período, por produto.</summary>
    Task<IReadOnlyList<LinhaMovimentacaoProdutoResumo>> ObterMovimentacoesProdutosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);
}
