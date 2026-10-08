using ImperialColors.Application.DTOs;

namespace ImperialColors.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardDto> ObterDadosDashboardAsync();

    /// <summary>Visão "Estoque" do Dashboard — produtos próximos da validade, com pouca
    /// quantidade, e mais vendidos do mês. Carregada sob demanda (só quando o operador troca
    /// para essa visão), não junto com o financeiro.</summary>
    Task<DashboardEstoqueDto> ObterVisaoEstoqueAsync(CancellationToken cancellationToken = default);

    /// <summary>Visão "Vendas" do Dashboard — maiores vendas do mês.</summary>
    Task<DashboardVendasDto> ObterVisaoVendasAsync(CancellationToken cancellationToken = default);

    /// <summary>Itens vendidos por categoria e os 5 produtos mais vendidos, no mês corrente ou no
    /// histórico inteiro. Carregado separado da visão para o seletor Mês/Total recarregar só
    /// estes dois blocos.</summary>
    Task<DashboardProdutosVendidosDto> ObterProdutosVendidosAsync(
        PeriodoDashboard periodo, CancellationToken cancellationToken = default);

    /// <summary>Painel de comissões de venda externa — a pagar, já pago e o total do mês,
    /// com a lista de pendentes.</summary>
    Task<ResumoComissoesDto> ObterVisaoComissoesAsync(CancellationToken cancellationToken = default);
}
