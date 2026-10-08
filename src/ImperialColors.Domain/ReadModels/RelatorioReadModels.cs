using ImperialColors.Domain.Enums;

namespace ImperialColors.Domain.ReadModels;

public class LinhaRelatorioVendaExternaResumo
{
    public DateTime DataVenda { get; set; }
    public string CodigoVenda { get; set; } = string.Empty;
    public string ProdutoItem { get; set; } = string.Empty;
    public decimal QuantidadeVendida { get; set; }
    public decimal ValorUnitario { get; set; }
    public decimal ValorTotal { get; set; }
}

public class ProdutoRankingResumo
{
    public string CodigoInterno { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;
    public decimal QuantidadeTotal { get; set; }
    public decimal FaturamentoGerado { get; set; }
}

/// <summary>
/// Faturamento e custo de UM dia, já agregados pelo banco. Existe para o dashboard não
/// precisar materializar o grafo <c>Venda → Itens → Produto</c> do mês inteiro só para somar
/// quatro cifras: um mês de 500 vendas/dia com 5 itens cada são ~112 mil linhas trazidas para
/// a memória a cada abertura do app, contra ~31 linhas deste resumo.
/// </summary>
public class ResumoVendasDiario
{
    public DateTime Data { get; set; }
    public int QuantidadeVendas { get; set; }
    public decimal Faturamento { get; set; }
    public decimal Custo { get; set; }

    /// <summary>Itens vendidos cujo produto não tem custo cadastrado — o lucro do dia está
    /// subestimado na proporção deles, e a tela avisa o operador quando é maior que zero.</summary>
    public int ItensSemCusto { get; set; }

    public decimal Lucro => Faturamento - Custo;
}

public class ProdutoEncalhadoResumo
{
    public string CodigoInterno { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;
    public decimal EstoqueAtual { get; set; }
    public decimal ValorTotalParado { get; set; }
}

/// <summary>
/// Totais de comissão de venda externa, agregados pelo banco. Separa o que ainda é dívida
/// com o vendedor (a pagar) do que já foi acertado, e mostra à parte quanto o mês corrente
/// gerou de comissão — que é o custo de vender na rua no período, pago ou não.
/// </summary>
public class ResumoComissoesVendaExterna
{
    public decimal TotalAPagar { get; set; }
    public int QuantidadeAPagar { get; set; }
    public decimal TotalPago { get; set; }
    public int QuantidadePaga { get; set; }
    public decimal TotalDoMes { get; set; }
}

/// <summary>
/// Uma linha de produto vendido, com o canal por onde a venda entrou. Granularidade de
/// ITEM, não de venda: a pergunta que o relatório responde é "o que cada produto rendeu em
/// cada canal", e isso não dá para extrair de um total por venda.
/// </summary>
public class LinhaVendaPorCanalResumo
{
    public DateTime DataVenda { get; set; }
    public CanalVenda Canal { get; set; }

    /// <summary>Id da venda de balcão, usado para cruzar com o registro da integração e
    /// descobrir quais vieram do site. Zero nas linhas de venda externa.</summary>
    public int VendaId { get; set; }

    /// <summary>Código interno do produto. Vazio no item manual da venda externa, que não
    /// tem produto cadastrado por trás — ver <see cref="ProdutoCadastrado"/>.</summary>
    public string CodigoProduto { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;
    public string NumeroVenda { get; set; } = string.Empty;
    public decimal Quantidade { get; set; }
    public decimal ValorUnitario { get; set; }
    public decimal ValorTotal { get; set; }

    /// <summary>Falso no item digitado à mão na venda externa. O relatório marca essas
    /// linhas porque elas entram no faturamento mas não têm histórico de estoque.</summary>
    public bool ProdutoCadastrado { get; set; }
}

/// <summary>
/// Uma movimentação de estoque já resolvida com o código do produto e com o documento que
/// a originou — é a linha do extrato que mostra quando o produto entrou e quando saiu.
/// </summary>
public class LinhaMovimentacaoProdutoResumo
{
    public DateTime Data { get; set; }

    /// <summary>Venda de balcão que originou a saída, quando houve. Usada para cruzar com o
    /// registro da integração e descobrir se a venda veio do site.</summary>
    public int? VendaId { get; set; }

    public string CodigoProduto { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;
    public string Unidade { get; set; } = string.Empty;
    public TipoMovimentacao Tipo { get; set; }
    public decimal Quantidade { get; set; }
    public decimal SaldoAnterior { get; set; }
    public decimal SaldoPosterior { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Usuario { get; set; }

    /// <summary>Número da venda que gerou a saída, quando veio de uma. Nulo em entrada de
    /// compra, estoque inicial e ajuste — que são justamente os casos em que o
    /// <see cref="Motivo"/> é a única explicação disponível.</summary>
    public string? NumeroVenda { get; set; }

    /// <summary>Canal da venda que gerou a saída. Nulo quando a movimentação não veio de
    /// venda.</summary>
    public CanalVenda? Canal { get; set; }
}

/// <summary>Itens vendidos de uma categoria no período, balcão e venda externa somados.</summary>
public class CategoriaItensVendidosResumo
{
    /// <summary>Nulo quando o item não tem categoria: produto sem categoria cadastrada ou item
    /// digitado à mão na venda externa, que não tem produto por trás.</summary>
    public string? Categoria { get; set; }
    public decimal QuantidadeItens { get; set; }
}

/// <summary>Produto entre os mais vendidos do período, com quantas vendas distintas o
/// incluíram — "vendeu 40 unidades" e "apareceu em 12 vendas" contam histórias diferentes
/// (uma obra grande infla a quantidade e quase não mexe no número de vendas).</summary>
public class ProdutoMaisVendidoResumo
{
    public string CodigoInterno { get; set; } = string.Empty;
    public string NomeProduto { get; set; } = string.Empty;
    public decimal QuantidadeVendida { get; set; }
    public int QuantidadeVendas { get; set; }
}
