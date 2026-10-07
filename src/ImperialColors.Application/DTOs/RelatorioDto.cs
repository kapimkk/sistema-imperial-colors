using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Helpers;

namespace ImperialColors.Application.DTOs;

public class LinhaRelatorioVendaExternaDto
{
    public DateTime DataVenda { get; set; }
    public string CodigoVenda { get; set; } = string.Empty;
    public string ProdutoItem { get; set; } = string.Empty;
    public decimal QuantidadeVendida { get; set; }
    public decimal ValorUnitario { get; set; }
    public decimal ValorTotal { get; set; }
}

public class ProdutoRankingDto
{
    public int Posicao { get; set; }
    public string CodigoInterno { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;
    public decimal QuantidadeTotal { get; set; }
    public decimal FaturamentoGerado { get; set; }
}

public class ProdutoEncalhadoDto
{
    public string CodigoInterno { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;
    public decimal EstoqueAtual { get; set; }
    public decimal ValorTotalParado { get; set; }
}

public enum TipoAnaliseGiroProduto
{
    MaisVendidos,
    MenosVendidos,
    NuncaVendidos
}

public class LinhaRelatorioVendaConsolidadaDto
{
    public DateTime DataVenda { get; set; }
    public string Origem { get; set; } = string.Empty;
    public string NumeroVenda { get; set; } = string.Empty;
    public string ClienteOuResumo { get; set; } = string.Empty;
    public int TotalItens { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Desconto { get; set; }

    /// <summary>Comissão paga ao vendedor (só venda externa). Fica em coluna própria, e não
    /// somada ao desconto: desconto é abatimento dado ao cliente, comissão é custo da loja —
    /// confundir os dois faria o relatório dizer que o cliente pagou menos do que pagou.</summary>
    public decimal Comissao { get; set; }

    /// <summary>Líquido: o que ficou para a loja depois do desconto e da comissão. É por ele
    /// que o relatório soma o faturamento, igual ao Dashboard.</summary>
    public decimal Total { get; set; }
    public string? FormaPagamento { get; set; }
}

/// <summary>Uma venda de um produto, com o canal por onde entrou — linha do relatório
/// "Vendas por Canal e Produto".</summary>
public class LinhaVendaPorCanalDto
{
    public DateTime DataVenda { get; set; }
    public CanalVenda Canal { get; set; }
    public string CanalDescricao => CanalVendaHelper.Descricao(Canal);
    public string CodigoProduto { get; set; } = string.Empty;

    /// <summary>O que vai na coluna de código: item manual da venda externa não tem produto
    /// cadastrado, e deixar a célula vazia faria parecer erro de geração.</summary>
    public string CodigoExibicao => ProdutoCadastrado && !string.IsNullOrWhiteSpace(CodigoProduto)
        ? CodigoProduto
        : "(sem cadastro)";

    public string NomeProduto { get; set; } = string.Empty;
    public string NumeroVenda { get; set; } = string.Empty;
    public decimal Quantidade { get; set; }
    public decimal ValorUnitario { get; set; }
    public decimal ValorTotal { get; set; }
    public bool ProdutoCadastrado { get; set; }
}

/// <summary>Totais de um canal no período — o rodapé que responde "quanto cada frente
/// rendeu" sem o lojista precisar somar as linhas.</summary>
public class TotalCanalDto
{
    public CanalVenda Canal { get; set; }
    public string CanalDescricao => CanalVendaHelper.Descricao(Canal);
    public int QuantidadeLinhas { get; set; }
    public decimal QuantidadeItens { get; set; }
    public decimal ValorTotal { get; set; }
}

/// <summary>Uma entrada ou saída de estoque — linha do relatório "Movimentação de
/// Produtos".</summary>
public class LinhaMovimentacaoProdutoDto
{
    public DateTime Data { get; set; }
    public string CodigoProduto { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;
    public string Unidade { get; set; } = string.Empty;
    public TipoMovimentacao Tipo { get; set; }

    public string TipoDescricao => Tipo switch
    {
        TipoMovimentacao.Entrada => "Entrada",
        TipoMovimentacao.Saida => "Saída",
        TipoMovimentacao.Ajuste => "Ajuste",
        _ => Tipo.ToString()
    };

    public decimal Quantidade { get; set; }

    /// <summary>Quantidade com sinal: saída aparece negativa para a coluna poder ser somada
    /// e dar o saldo movimentado no período.</summary>
    public decimal QuantidadeComSinal => Tipo == TipoMovimentacao.Saida ? -Quantidade : Quantidade;

    public decimal SaldoAnterior { get; set; }
    public decimal SaldoPosterior { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Usuario { get; set; }
    public string? NumeroVenda { get; set; }
    public CanalVenda? Canal { get; set; }

    /// <summary>Origem da movimentação em uma coluna só: o documento quando veio de venda,
    /// senão o motivo (compra, estoque inicial, ajuste).</summary>
    public string OrigemDescricao => NumeroVenda is { Length: > 0 }
        ? $"{NumeroVenda}{(Canal is { } c ? $" — {CanalVendaHelper.Descricao(c)}" : string.Empty)}"
        : (string.IsNullOrWhiteSpace(Motivo) ? "—" : Motivo);
}
