using ImperialColors.Domain.Enums;

namespace ImperialColors.Domain.Helpers;

/// <summary>
/// Nome de exibição dos canais de venda, em um lugar só: o mesmo texto aparece no relatório
/// por canal e no de movimentação. Escrito em cada tela, uma venda apareceria como "Rua" num
/// relatório e "Venda externa" no outro.
/// </summary>
public static class CanalVendaHelper
{
    public static string Descricao(CanalVenda canal) => canal switch
    {
        CanalVenda.LojaFisica => "Loja física",
        CanalVenda.VendaExternaRua => "Venda externa (Rua)",
        CanalVenda.Site => "Site",
        _ => canal.ToString()
    };

    /// <summary>Ordem fixa para os totais por canal saírem sempre na mesma sequência,
    /// independentemente do que apareceu no período — dois relatórios de meses diferentes
    /// precisam poder ser comparados linha a linha.</summary>
    public static IReadOnlyList<CanalVenda> OrdemRelatorio { get; } =
        [CanalVenda.LojaFisica, CanalVenda.VendaExternaRua, CanalVenda.Site];
}
