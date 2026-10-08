using ImperialColors.Application.Helpers;

namespace ImperialColors.Application.DTOs;

public class ProdutoDto
{
    public int Id { get; set; }
    public string CodigoInterno { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int? CategoriaId { get; set; }
    public string? CategoriaNome { get; set; }
    public int? MarcaId { get; set; }
    public string? MarcaNome { get; set; }
    public decimal QuantidadeEstoque { get; set; }
    public decimal EstoqueMinimo { get; set; }
    public string Unidade { get; set; } = "UN";
    public string? TamanhoEmbalagem { get; set; }
    /// <summary>Peso da unidade em gramas — ver <see cref="Domain.Entities.Produto.PesoGramas"/>.</summary>
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
    public string? FornecedorNome { get; set; }
    public string? Observacoes { get; set; }

    public bool EstoqueBaixo => QuantidadeEstoque <= EstoqueMinimo && QuantidadeEstoque > 0;
    public bool SemEstoque => QuantidadeEstoque <= 0;
    public bool EmPromocao => ProdutoPrecoHelper.EstaEmPromocao(PromocaoAtiva, PrecoPromocional, PrecoVenda);
    public decimal PrecoEfetivo => ProdutoPrecoHelper.ObterPrecoEfetivo(PrecoVenda, PromocaoAtiva, PrecoPromocional);

    /// <summary>Nome para exibição com o tamanho da embalagem (ex.: "Tinta Coral (18L)",
    /// "Selador (25 KG)") — funciona para qualquer unidade, não só Galão.</summary>
    public string NomeExibicao => !string.IsNullOrWhiteSpace(TamanhoEmbalagem)
        ? $"{Nome} ({TamanhoEmbalagem})"
        : Nome;

    /// <summary>Peso para leitura ("5,5 kg", "800 g") — vazio quando não cadastrado.</summary>
    public string PesoFormatado => PesoProdutoHelper.Formatar(PesoGramas);
}

public class CriarProdutoDto
{
    public string CodigoInterno { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string Nome { get; set; } = string.Empty;
    public int? CategoriaId { get; set; }
    public int? MarcaId { get; set; }
    public decimal QuantidadeEstoque { get; set; }
    public decimal EstoqueMinimo { get; set; }
    public string Unidade { get; set; } = "UN";
    public string? TamanhoEmbalagem { get; set; }
    /// <summary>Peso da unidade em gramas — ver <see cref="Domain.Entities.Produto.PesoGramas"/>.</summary>
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
    /// <summary>Arquivo local escolhido, somente entrada transitória; nunca persistido no banco.</summary>
    public string? ImagemArquivoSelecionado { get; set; }
    public bool AlterarImagem { get; set; }
    public bool RemoverImagem { get; set; }
    public bool CodigoInternoDefinidoManualmente { get; set; }
}

public class AtualizarProdutoDto : CriarProdutoDto
{
    public int Id { get; set; }

    /// <summary>
    /// Quantidade em estoque que a tela de edição exibia quando foi carregada (antes de
    /// qualquer alteração do usuário). Usada para calcular o delta real pretendido —
    /// <see cref="CriarProdutoDto.QuantidadeEstoque"/> (o que está no campo agora) menos
    /// este valor — em vez de tratar o campo como um valor absoluto a sobrescrever.
    /// Isso evita que salvar uma edição que não tocou no campo de quantidade apague uma
    /// venda concorrente feita no PDV enquanto a tela estava aberta. Se não informado
    /// (null), o serviço usa o valor atual do banco como baseline (mesmo comportamento,
    /// só que sem proteção contra o cenário de "tela aberta por um tempo").
    /// </summary>
    public decimal? QuantidadeEstoqueOriginal { get; set; }
}
