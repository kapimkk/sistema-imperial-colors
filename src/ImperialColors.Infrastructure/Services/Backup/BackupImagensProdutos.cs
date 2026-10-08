using System.Security.Cryptography;
using System.Text.Json;
using ImperialColors.Domain.Exceptions;

namespace ImperialColors.Infrastructure.Services.Backup;

/// <summary>Pacote portátil de imagens; referências continuam relativas ao restaurar em outro PC.</summary>
public static class BackupImagensProdutos
{
    public const string Manifesto = "imagens_produtos_manifest.json";
    public sealed record Arquivo(string Referencia, long Bytes, string Sha256);
    public sealed record Pacote(int Versao, IReadOnlyList<Arquivo> Arquivos);

    public static async Task CopiarAsync(string raiz, string destino, CancellationToken cancellationToken = default)
    {
        var storage = new ImagemProdutoStorage(raiz);
        var pasta = Path.Combine(raiz, ImagemProdutoStorage.Pasta);
        var copia = Path.Combine(destino, ImagemProdutoStorage.Pasta);
        if (Directory.Exists(pasta) && Directory.EnumerateDirectories(pasta).Any())
            throw new DomainException("A pasta de imagens contém subpastas inesperadas. Verifique o catálogo antes do backup.");
        Directory.CreateDirectory(copia);
        var arquivos = new List<Arquivo>();
        foreach (var origem in Directory.EnumerateFiles(pasta).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var referencia = ImagemProdutoStorage.Pasta + "/" + Path.GetFileName(origem);
            _ = storage.ObterCaminhoSeguro(referencia);
            storage.ValidarArquivo(origem);
            var arquivoDestino = Path.Combine(copia, Path.GetFileName(origem));
            File.Copy(origem, arquivoDestino, overwrite: true);
            await using var stream = File.OpenRead(arquivoDestino);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
            arquivos.Add(new Arquivo(referencia, stream.Length, hash));
        }
        await File.WriteAllTextAsync(Path.Combine(destino, Manifesto),
            JsonSerializer.Serialize(new Pacote(1, arquivos), new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    // Restauração é explícita em raiz vazia: nunca sobrescreve o catálogo ativo por acidente.
    public static Task RestaurarAsync(string backup, string raizDestino, CancellationToken cancellationToken = default)
        => RestaurarValidadoAsync(backup, raizDestino, CopiarArquivoAsync, cancellationToken);

    // Separa o transporte de bytes para testar falhas de disco/cancelamento durante a cópia.
    internal static async Task RestaurarValidadoAsync(string backup, string raizDestino,
        Func<string, string, CancellationToken, Task> copiar, CancellationToken cancellationToken = default)
    {
        var pacote = JsonSerializer.Deserialize<Pacote>(await File.ReadAllTextAsync(Path.Combine(backup, Manifesto), cancellationToken))
            ?? throw new DomainException("Manifesto de imagens inválido.");
        if (pacote.Versao != 1 || pacote.Arquivos is null)
            throw new DomainException("Versão do manifesto de imagens inválida.");
        var origem = new ImagemProdutoStorage(backup);
        var destino = new ImagemProdutoStorage(raizDestino);
        var raizCompleta = Path.GetFullPath(raizDestino);
        var pastaDestino = Path.Combine(raizCompleta, ImagemProdutoStorage.Pasta);
        var destinoExistia = Directory.Exists(pastaDestino);
        if (destinoExistia && Directory.EnumerateFileSystemEntries(pastaDestino).Any())
            throw new DomainException("Restaure as imagens em um diretório vazio, sem substituir o catálogo ativo.");
        var referencias = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Confere todos os arquivos ANTES de criar staging: corrupção/traversal falham fechados.
        foreach (var arquivo in pacote.Arquivos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(arquivo.Referencia) || !referencias.Add(arquivo.Referencia))
                throw new DomainException("Referência inválida ou duplicada no manifesto de imagens.");
            var caminho = origem.ObterCaminhoSeguro(arquivo.Referencia)!;
            _ = destino.ObterCaminhoSeguro(arquivo.Referencia);
            await ConferirAsync(origem, caminho, arquivo, cancellationToken);
        }

        using var bloqueio = await destino.AdquirirBloqueioAsync(cancellationToken);
        if (Directory.EnumerateFileSystemEntries(pastaDestino).Any())
            throw new DomainException("O destino foi alterado durante a restauração. Use uma raiz vazia.");
        // Irmã do destino: rename no mesmo volume publica o conjunto inteiro, nunca arquivo a arquivo.
        var staging = Path.GetFullPath(Path.Combine(raizCompleta, ".ImagensProdutos.restore-" + Guid.NewGuid().ToString("N")));
        if (!string.Equals(Path.GetDirectoryName(staging), raizCompleta.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Diretório temporário de restauração inválido.");
        var promovido = false;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var arquivo in pacote.Arquivos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = origem.ObterCaminhoSeguro(arquivo.Referencia)!;
                var stagedFile = Path.Combine(staging, Path.GetFileName(source));
                await copiar(source, stagedFile, cancellationToken);
                // Revalida a cópia contra manifesto, detectando mudança/corrupção após o preflight.
                await ConferirAsync(origem, stagedFile, arquivo, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(pastaDestino); // já conferida vazia e protegida pelo lock
            Directory.Move(staging, pastaDestino);
            promovido = true;
        }
        finally
        {
            // Só apaga nosso staging gerado sob a raiz conferida; nunca o catálogo ativo.
            if (!promovido && Directory.Exists(staging)
                && (File.GetAttributes(staging) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(staging, recursive: true);
            if (!promovido && !destinoExistia && Directory.Exists(pastaDestino)
                && !Directory.EnumerateFileSystemEntries(pastaDestino).Any()
                && (File.GetAttributes(pastaDestino) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(pastaDestino);
        }
    }

    private static async Task ConferirAsync(ImagemProdutoStorage storage, string caminho, Arquivo arquivo, CancellationToken cancellationToken)
    {
        storage.ValidarArquivo(caminho);
        await using var stream = File.OpenRead(caminho);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        if (stream.Length != arquivo.Bytes || !string.Equals(hash, arquivo.Sha256, StringComparison.Ordinal))
            throw new DomainException("O backup de imagens está corrompido ou incompleto.");
    }

    private static async Task CopiarArquivoAsync(string origem, string destino, CancellationToken cancellationToken)
    {
        await using var entrada = new FileStream(origem, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous);
        await using var saida = new FileStream(destino, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
        await entrada.CopyToAsync(saida, cancellationToken);
        await saida.FlushAsync(cancellationToken);
    }
}
