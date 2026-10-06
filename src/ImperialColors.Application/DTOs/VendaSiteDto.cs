using System.Globalization;
using ImperialColors.Domain.ReadModels;

namespace ImperialColors.Application.DTOs;

/// <summary>
/// Uma linha da tela "Vendas Site": o pedido pago no e-commerce, o número da venda que o
/// ImperialSync criou para ele neste banco e o que o operador precisa conferir (cliente, data,
/// total, pagamento, parcelas, status e quando foi sincronizada).
/// </summary>
public class VendaSiteDto
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public Guid OperacaoId { get; set; }

    /// <summary>Número do pedido no site (ex.: IC-2026-000123).</summary>
    public string PedidoSite { get; set; } = string.Empty;

    public int VendaId { get; set; }
    public string NumeroVenda { get; set; } = string.Empty;

    /// <summary>Falso quando a venda foi apagada do banco depois de sincronizada.</summary>
    public bool VendaExiste { get; set; }

    public string Cliente { get; set; } = string.Empty;
    public DateTime? DataVenda { get; set; }
    public decimal? Total { get; set; }

    /// <summary>"Pix", "Cartão de Crédito - 3x"... ou "—" quando a venda não existe mais.</summary>
    public string Pagamento { get; set; } = string.Empty;

    public int? Parcelas { get; set; }

    /// <summary>"Finalizada", "Cancelada", "Aberta", "Excluída" ou "Venda removida".</summary>
    public string StatusDescricao { get; set; } = string.Empty;

    public DateTime SincronizadoEm { get; set; }

    // Textos prontos para a grade: a venda removida não tem data, total nem parcelas, e "R$ 0,00"
    // ou uma célula vazia passariam a impressão de que existe e é zero. A coluna ordena pelo
    // valor de verdade (SortMemberPath), não por este texto.
    public string DataVendaDescricao => DataVenda?.ToString("dd/MM/yyyy HH:mm", PtBr) ?? "—";
    public string TotalDescricao => Total?.ToString("C2", PtBr) ?? "—";
    public string ParcelasDescricao => Parcelas is > 0 ? $"{Parcelas}x" : "—";
    public string SincronizadoEmDescricao => SincronizadoEm.ToString("dd/MM/yyyy HH:mm", PtBr);
}

/// <summary>Uma página de vendas do site, com a situação da integração para a tela explicar
/// "não instalada" e "sem permissão" em vez de mostrar uma lista vazia sem motivo.</summary>
public class ResultadoVendasSiteDto
{
    public SituacaoIntegracaoSite Situacao { get; init; } = SituacaoIntegracaoSite.Disponivel;
    public PaginacaoResultadoDto<VendaSiteDto> Pagina { get; init; } = new();
}
