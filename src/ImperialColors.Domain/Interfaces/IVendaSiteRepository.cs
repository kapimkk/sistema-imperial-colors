using ImperialColors.Domain.ReadModels;

namespace ImperialColors.Domain.Interfaces;

/// <summary>
/// Leitura das vendas que vieram do e-commerce. SOMENTE LEITURA: quem cria a venda online é a
/// função <c>integration.apply_sale_create</c>, chamada pelo ImperialSync — este sistema não
/// grava nada nessas tabelas.
/// </summary>
public interface IVendaSiteRepository
{
    /// <summary>
    /// Vendas do site, da sincronização mais recente para a mais antiga, paginadas no banco.
    /// A lista nasce do registro da integração, então só aparece venda que veio do site — nunca
    /// uma venda de balcão. <paramref name="termoBusca"/> procura no número do pedido do site,
    /// no número da venda e no nome do comprador.
    /// </summary>
    Task<PaginaVendasSite> ObterPaginadoAsync(
        int pagina, int itensPorPagina, string? termoBusca = null, CancellationToken cancellationToken = default);
}
