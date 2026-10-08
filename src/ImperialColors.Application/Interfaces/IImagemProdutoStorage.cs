namespace ImperialColors.Application.Interfaces;

/// <summary>Arquivos locais do catálogo; o serviço confirma o banco antes de retirar o arquivo anterior.</summary>
public interface IImagemProdutoStorage
{
    Task<IDisposable> AdquirirBloqueioAsync(CancellationToken cancellationToken = default);
    void ValidarArquivo(string arquivo);
    Task<string> ImportarAsync(string arquivo, CancellationToken cancellationToken = default);
    string? ObterCaminhoSeguro(string? referencia);
    void RemoverSeExistir(string? referencia);
}