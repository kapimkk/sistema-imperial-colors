using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Exceptions;

namespace ImperialColors.Infrastructure.Services;

/// <summary>JPEG/PNG decodificados, referências geradas e raiz da instalação ou compartilhada configurada.</summary>
public sealed partial class ImagemProdutoStorage : IImagemProdutoStorage
{
    public const int TamanhoMaximoBytes = 5_000_000;
    public const int LadoMaximo = 8000;
    public const long PixelsMaximos = 40_000_000;
    public const string Pasta = "ImagensProdutos";
    private readonly string _raiz;
    private string Diretorio => Path.Combine(_raiz, Pasta);

    public ImagemProdutoStorage() : this(ResolverRaiz(AppContext.BaseDirectory,
        Environment.GetEnvironmentVariable("PRODUCT_IMAGES_ROOT"))) { }
    public ImagemProdutoStorage(string raiz) => _raiz = ResolverRaiz(raiz, null);

    // A configuração é local e confiável; o banco continua guardando apenas ImagensProdutos/nome.
    public static string ResolverRaiz(string instalacao, string? configurada)
    {
        if (string.IsNullOrWhiteSpace(configurada)) return Path.GetFullPath(instalacao);
        if (!Path.IsPathFullyQualified(configurada))
            throw new DomainException("PRODUCT_IMAGES_ROOT deve indicar uma pasta absoluta ou compartilhamento UNC.");
        var raiz = Path.GetFullPath(configurada.Trim());
        if (!Directory.Exists(raiz))
            throw new DomainException("A pasta PRODUCT_IMAGES_ROOT está indisponível. Verifique o compartilhamento; não será usada uma cópia local.");
        if ((File.GetAttributes(raiz) & FileAttributes.ReparsePoint) != 0)
            throw new DomainException("PRODUCT_IMAGES_ROOT não pode ser um link externo.");
        return raiz;
    }

    public async Task<IDisposable> AdquirirBloqueioAsync(CancellationToken cancellationToken = default)
    {
        GarantirDiretorioSeguro();
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // FileShare.None é coordenado pelo servidor SMB e liberado pelo SO após crash.
                var caminho = Path.Combine(_raiz, ".imperial-catalog.lock");
                if (File.Exists(caminho) && (File.GetAttributes(caminho) & FileAttributes.ReparsePoint) != 0)
                    throw new DomainException("Arquivo de bloqueio do catálogo inválido.");
                return new FileStream(caminho, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < limite)
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (IOException)
            {
                throw new DomainException("O catálogo de imagens está ocupado ou indisponível. Tente novamente em instantes.");
            }
        }
    }

    public void ValidarArquivo(string arquivo)
    {
        var info = new FileInfo(arquivo);
        if (!info.Exists)
            throw new DomainException("A imagem selecionada não foi encontrada.");
        if (info.Length <= 0 || info.Length > TamanhoMaximoBytes)
            throw new DomainException("A imagem deve ter no máximo 5 MB.");
        var extensao = Path.GetExtension(arquivo).ToLowerInvariant();
        if (extensao is not (".jpg" or ".jpeg" or ".png"))
            throw new DomainException("Selecione uma imagem JPEG ou PNG.");
        try
        {
            using var stream = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> cabecalho = stackalloc byte[8];
            if (stream.Read(cabecalho) != cabecalho.Length)
                throw new DomainException("Arquivo de imagem inválido ou corrompido.");
            var png = cabecalho.SequenceEqual(new byte[] {137, 80, 78, 71, 13, 10, 26, 10});
            var jpeg = cabecalho[0] == 0xff && cabecalho[1] == 0xd8 && cabecalho[2] == 0xff;
            if ((extensao == ".png" && !png) || (extensao != ".png" && !jpeg))
                throw new DomainException("O conteúdo da imagem não corresponde ao formato informado.");
            stream.Position = 0;
            using var imagem = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if ((png && imagem.RawFormat.Guid != ImageFormat.Png.Guid)
                || (jpeg && imagem.RawFormat.Guid != ImageFormat.Jpeg.Guid))
                throw new DomainException("Formato da imagem inválido.");
            if (imagem.Width > LadoMaximo || imagem.Height > LadoMaximo
                || (long)imagem.Width * imagem.Height > PixelsMaximos)
                throw new DomainException("A imagem excede 8000 pixels por lado ou 40 megapixels.");
            // Decodifica os pixels para rejeitar cabeçalho válido com corpo truncado/corrompido.
            using var pixels = new Bitmap(imagem);
            _ = pixels.GetPixel(pixels.Width - 1, pixels.Height - 1);
        }
        catch (DomainException) { throw; }
        catch (Exception e) when (e is ArgumentException or OutOfMemoryException or IOException or System.Runtime.InteropServices.ExternalException)
        {
            throw new DomainException("Arquivo de imagem inválido, corrompido ou indisponível.");
        }
    }

    public async Task<string> ImportarAsync(string arquivo, CancellationToken cancellationToken = default)
    {
        ValidarArquivo(arquivo);
        GarantirDiretorioSeguro();
        var extensao = Path.GetExtension(arquivo).Equals(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        var nome = Guid.NewGuid().ToString("N") + extensao;
        var referencia = Pasta + "/" + nome;
        var destino = ObterCaminhoSeguro(referencia)!;
        try
        {
            await using (var entrada = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var saida = new FileStream(destino, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                // Revalida a cópia: a origem pode ser substituída entre seleção e salvamento.
                var buffer = new byte[64 * 1024];
                var total = 0;
                int lidos;
                while ((lidos = await entrada.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += lidos;
                    if (total > TamanhoMaximoBytes)
                        throw new DomainException("A imagem deve ter no máximo 5 MB.");
                    await saida.WriteAsync(buffer.AsMemory(0, lidos), cancellationToken);
                }
                await saida.FlushAsync(cancellationToken);
            }
            ValidarArquivo(destino);
            return referencia;
        }
        catch
        {
            if (File.Exists(destino))
                File.Delete(destino);
            throw;
        }
    }

    public string? ObterCaminhoSeguro(string? referencia)
    {
        if (string.IsNullOrWhiteSpace(referencia))
            return null;
        if (!ReferenciaRegex().IsMatch(referencia))
            throw new DomainException("Referência de imagem inválida.");
        var diretorio = Path.GetFullPath(Diretorio);
        if (Directory.Exists(diretorio) && (File.GetAttributes(diretorio) & FileAttributes.ReparsePoint) != 0)
            throw new DomainException("A pasta de imagens não pode ser um link externo.");
        var caminho = Path.Combine(diretorio, referencia[(Pasta.Length + 1)..]);
        if (File.Exists(caminho) && (File.GetAttributes(caminho) & FileAttributes.ReparsePoint) != 0)
            throw new DomainException("A imagem não pode ser um link externo.");
        return caminho;
    }

    public void RemoverSeExistir(string? referencia)
    {
        var caminho = ObterCaminhoSeguro(referencia);
        if (caminho is not null && File.Exists(caminho))
            File.Delete(caminho);
    }

    private void GarantirDiretorioSeguro()
    {
        // Nunca recria uma raiz configurada/compartilhamento ausente como uma pasta local.
        if (!Directory.Exists(_raiz))
            throw new DomainException("A pasta de imagens está indisponível. Verifique o compartilhamento.");
        if (Directory.Exists(Diretorio) && (File.GetAttributes(Diretorio) & FileAttributes.ReparsePoint) != 0)
            throw new DomainException("A pasta de imagens não pode ser um link externo.");
        Directory.CreateDirectory(Diretorio);
    }

    [GeneratedRegex(@"^ImagensProdutos/[a-f0-9]{32}\.(jpg|png)$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenciaRegex();
}