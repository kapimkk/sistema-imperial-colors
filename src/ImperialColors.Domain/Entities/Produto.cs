namespace ImperialColors.Domain.Entities;

public class Produto : BaseEntity
{
    public string CodigoInterno { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int? CategoriaId { get; set; }
    public int? MarcaId { get; set; }
    public decimal QuantidadeEstoque { get; set; }
    public decimal EstoqueMinimo { get; set; }
    public string Unidade { get; set; } = "UN";
    // Tamanho/capacidade da embalagem, texto livre (ex.: "18L", "3,6L", "25 KG") — não
    // preso a uma unidade específica nem a uma lista fechada de valores. Veio da
    // importação do catálogo Paraná: o fornecedor descreve a mesma tinta em bombona 16L,
    // 18L, balde de 17kg ou 25kg — um decimal fixo (como a antiga LitragemGl, restrita a
    // GL=3,6/18) não desse conta de "kg" nem de embalagens que o cliente ainda nem usa.
    public string? TamanhoEmbalagem { get; set; }

    /// <summary>
    /// Peso exato da unidade em gramas (int32). A tela aceita kg com até três casas
    /// decimais e converte sem arredondar. Nulo preserva cadastros legados incompletos;
    /// novos produtos exigem peso e dimensões positivos.
    /// </summary>
    public int? PesoGramas { get; set; }
    public decimal? AlturaCm { get; set; }
    public decimal? LarguraCm { get; set; }
    public decimal? ComprimentoCm { get; set; }
    public string? ImagemProdutoPath { get; set; }
    /// <summary>Remoção explícita; arquivo ausente nunca equivale a ordem de excluir no site.</summary>
    public bool ImagemRemovida { get; set; }

    public decimal? Custo { get; set; }
    public decimal PrecoVenda { get; set; }
    public bool PromocaoAtiva { get; set; }
    public decimal? PrecoPromocional { get; set; }
    public DateTime? DataValidade { get; set; }
    public int? FornecedorId { get; set; }
    public string? Observacoes { get; set; }

    public Categoria? Categoria { get; set; }
    public Marca? Marca { get; set; }
    public Fornecedor? Fornecedor { get; set; }
    public TributacaoProduto? Tributacao { get; set; }
    public ICollection<MovimentacaoEstoque> Movimentacoes { get; set; } = new List<MovimentacaoEstoque>();
    public ICollection<ItemVenda> ItensVenda { get; set; } = new List<ItemVenda>();
    public ICollection<ItemListaCompra> ItensListaCompra { get; set; } = new List<ItemListaCompra>();

    public bool EstoqueBaixo => QuantidadeEstoque <= EstoqueMinimo;
    public bool SemEstoque => QuantidadeEstoque <= 0;
}
