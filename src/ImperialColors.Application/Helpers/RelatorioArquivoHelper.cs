namespace ImperialColors.Application.Helpers;

/// <summary>
/// Cópia arquivada dos relatórios gerados, em pasta por mês e dia — a mesma estrutura do
/// backup automático, para quem já conhece uma saber achar a outra.
///
/// Existe porque os dois relatórios de controle (vendas por canal e movimentação) são
/// tirados período a período e perdem o valor se cada arquivo for parar numa pasta
/// diferente conforme o humor de quem exportou: o histórico é o produto, não o arquivo
/// avulso. O operador continua escolhendo onde salvar a sua cópia; esta é a de arquivo.
/// </summary>
public static class RelatorioArquivoHelper
{
    public const string Variavel = "RELATORIOS_PATH";
    public const string DiretorioRaizPadrao = @"C:\relatorios_sistema";

    public static string ObterDiretorioRaiz()
        => Environment.GetEnvironmentVariable(Variavel)?.Trim() is { Length: > 0 } valor
            ? valor
            : DiretorioRaizPadrao;

    /// <summary>
    /// Guarda uma cópia de <paramref name="caminhoGerado"/> no arquivo do dia e devolve o
    /// caminho dela.
    ///
    /// Devolve <c>null</c> em vez de estourar quando não consegue gravar (pasta sem
    /// permissão, disco cheio, caminho de rede fora do ar): o relatório que o operador
    /// pediu já está salvo onde ele escolheu, e derrubar a operação inteira por causa da
    /// cópia de arquivo trocaria um incômodo por um erro.
    /// </summary>
    public static string? ArquivarCopia(string caminhoGerado, DateTime agora, string? diretorioRaiz = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(caminhoGerado) || !File.Exists(caminhoGerado))
                return null;

            var destino = MontarCaminhoArquivado(caminhoGerado, agora, diretorioRaiz);

            // Mesmo relatório gerado duas vezes no mesmo dia: a segunda substitui a
            // primeira. Guardar as duas encheria a pasta de versões que ninguém distingue,
            // e a última é a que vale.
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.Copy(caminhoGerado, destino, overwrite: true);
            return destino;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
                                      or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    public static string MontarCaminhoArquivado(string caminhoGerado, DateTime agora, string? diretorioRaiz = null)
    {
        var pasta = BackupPathHelper.MontarPastaDestino(diretorioRaiz ?? ObterDiretorioRaiz(), agora);
        return Path.Combine(pasta, Path.GetFileName(caminhoGerado));
    }
}
