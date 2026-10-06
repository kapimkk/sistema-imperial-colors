using ImperialColors.Application.DTOs;

namespace ImperialColors.Application.Interfaces;

public interface IVendaSiteService
{
    /// <summary>
    /// Vendas que o ImperialSync criou neste banco a partir de pedidos pagos no site, da mais
    /// recente para a mais antiga. Só leitura: o sistema nunca cria nem altera venda online.
    /// </summary>
    Task<ResultadoVendasSiteDto> ObterPaginadoAsync(
        int pagina, int itensPorPagina, string? termoBusca = null, CancellationToken cancellationToken = default);
}
