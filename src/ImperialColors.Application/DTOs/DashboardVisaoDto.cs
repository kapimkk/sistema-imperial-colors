namespace ImperialColors.Application.DTOs;

/// <summary>Dados da visão "Estoque" do Dashboard — reaproveita <see cref="ProdutoDto"/> e
/// <see cref="ProdutoRankingDto"/> já existentes, sem duplicar mapeamento.</summary>
public class DashboardEstoqueDto
{
    public List<ProdutoDto> ProximosDaValidade { get; set; } = new();
    public List<ProdutoDto> PoucaQuantidade { get; set; } = new();
    public List<ProdutoRankingDto> MaisVendidos { get; set; } = new();
}

/// <summary>Dados da visão "Vendas" do Dashboard.</summary>
public class DashboardVendasDto
{
    public List<VendaDestaqueDto> MaioresVendas { get; set; } = new();
}

/// <summary>Recorte de tempo dos blocos de produtos vendidos na aba Vendas.</summary>
public enum PeriodoDashboard
{
    /// <summary>Do dia 1 do mês corrente até o fim dele.</summary>
    Mes = 1,

    /// <summary>Tudo que já foi vendido, desde o início do histórico.</summary>
    Total = 2
}

/// <summary>Itens por categoria e os cinco produtos que mais saíram, no período escolhido.
/// Separado de <see cref="DashboardVendasDto"/> porque este recorte muda com o seletor
/// Mês/Total e recarrega sozinho — as "Maiores Vendas" continuam sendo do mês.</summary>
public class DashboardProdutosVendidosDto
{
    public PeriodoDashboard Periodo { get; set; } = PeriodoDashboard.Mes;
    public List<CategoriaItensVendidosDto> ItensPorCategoria { get; set; } = new();
    public List<ProdutoMaisVendidoDto> ProdutosMaisVendidos { get; set; } = new();
}

public class CategoriaItensVendidosDto
{
    public string Categoria { get; set; } = string.Empty;
    public decimal QuantidadeItens { get; set; }

    /// <summary>0–100, proporcional à categoria que mais vendeu — só para a largura da barra.</summary>
    public decimal PercentualBarra { get; set; }
}

public class ProdutoMaisVendidoDto
{
    public int Posicao { get; set; }
    public string CodigoInterno { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;

    /// <summary>Unidades vendidas.</summary>
    public decimal QuantidadeVendida { get; set; }

    /// <summary>Em quantas vendas distintas o produto apareceu.</summary>
    public int QuantidadeVendas { get; set; }
}

public class VendaDestaqueDto
{
    public DateTime Data { get; set; }
    public string ClienteNome { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string FormaPagamentoDescricao { get; set; } = string.Empty;
}
