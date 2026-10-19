using ImperialColors.Application.DTOs;

namespace ImperialColors.Application.Interfaces;

public interface IProdutoService
{
    Task<IEnumerable<ProdutoDto>> ObterTodosAsync();
    Task<ProdutoDto?> ObterPorIdAsync(int id);
    Task<ProdutoDto?> ObterPorCodigoBarrasAsync(string codigoBarras);
    Task<ProdutoDto?> ObterPorCodigoInternoAsync(string codigoInterno);
    Task<IEnumerable<ProdutoDto>> BuscarAsync(string termo);
    Task<ProdutoDto> CriarAsync(CriarProdutoDto dto);
    Task<ProdutoDto> AtualizarAsync(int id, AtualizarProdutoDto dto);
    Task RemoverAsync(int id);
    Task<bool> CodigoBarrasExisteAsync(string codigoBarras, int? ignorarProdutoId = null, CancellationToken cancellationToken = default);
    Task<PaginacaoResultadoDto<ProdutoDto>> ObterPaginadoAsync(
        int pagina,
        int itensPorPagina,
        string? termoBusca = null,
        bool apenasPromocao = false,
        IReadOnlyCollection<string>? unidades = null,
        CancellationToken cancellationToken = default);
    Task<IEnumerable<ProdutoDto>> ObterComEstoqueBaixoAsync();
    Task<IEnumerable<ProdutoDto>> ObterSemEstoqueAsync();

    /// <summary>Produtos com saldo em estoque cuja validade vence em até
    /// <paramref name="diasLimite"/> dias (inclui já vencidos) — usado no relatório
    /// "Produtos Próximos da Validade".</summary>
    Task<IEnumerable<ProdutoDto>> ObterProximosDaValidadeAsync(int diasLimite = 15);
    Task RegistrarMovimentacaoAsync(MovimentacaoEstoqueDto dto);
    Task<string> GerarProximoCodigoInternoAsync();
    Task<string> GerarCodigoInternoPorNomeAsync(string nome, CancellationToken cancellationToken = default);

    /// <summary>Retorna a tributação do produto, ou um DTO vazio (ProdutoId preenchido) se nunca foi cadastrada.</summary>
    Task<TributacaoProdutoDto> ObterTributacaoAsync(int produtoId, CancellationToken cancellationToken = default);

    /// <summary>Valida (formato + regra CST×CSOSN conforme regime tributário) e salva a tributação do produto.</summary>
    Task<TributacaoProdutoDto> SalvarTributacaoAsync(int produtoId, TributacaoProdutoDto dto, CancellationToken cancellationToken = default);
}
