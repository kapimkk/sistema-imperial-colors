using ImperialColors.Application.DTOs;

namespace ImperialColors.Application.Interfaces;

public interface IRelatorioAnalyticsService
{
    Task<IReadOnlyList<LinhaRelatorioVendaExternaDto>> ObterLinhasVendasExternasAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProdutoRankingDto>> ObterRankingProdutosAsync(
        DateTime inicio, DateTime fim, TipoAnaliseGiroProduto tipo, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProdutoEncalhadoDto>> ObterProdutosEncalhadosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LinhaRelatorioVendaConsolidadaDto>> ObterVendasConsolidadasAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    /// <summary>Vendas do período por produto e canal (loja física, rua, site).</summary>
    Task<IReadOnlyList<LinhaVendaPorCanalDto>> ObterVendasPorCanalAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    /// <summary>Itens vendidos por categoria no período.</summary>
    Task<IReadOnlyList<CategoriaItensVendidosDto>> ObterItensVendidosPorCategoriaAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    /// <summary>Produtos mais vendidos do período, com a quantidade de vendas de cada um.</summary>
    Task<IReadOnlyList<ProdutoMaisVendidoDto>> ObterProdutosMaisVendidosComVendasAsync(
        DateTime inicio, DateTime fim, int quantidade, CancellationToken cancellationToken = default);

    /// <summary>Entradas e saídas de estoque do período, por produto e data.</summary>
    Task<IReadOnlyList<LinhaMovimentacaoProdutoDto>> ObterMovimentacoesProdutosAsync(
        DateTime inicio, DateTime fim, CancellationToken cancellationToken = default);

    /// <summary>Totais por canal, na ordem fixa do relatório. Canal sem venda no período
    /// sai zerado em vez de sumir: "o site não vendeu nada" é informação.</summary>
    IReadOnlyList<TotalCanalDto> TotalizarPorCanal(IEnumerable<LinhaVendaPorCanalDto> linhas);
}
