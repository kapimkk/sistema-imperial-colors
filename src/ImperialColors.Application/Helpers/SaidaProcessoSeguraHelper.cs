using System.Text.RegularExpressions;

namespace ImperialColors.Application.Helpers;

/// <summary>
/// Filtro do que o ImperialSync escreve no console antes de aparecer na tela ou no log.
///
/// O ImperialSync já nunca imprime senha, segredo, assinatura, CPF/CNPJ nem string de conexão —
/// mas este sistema não confia nisso para decidir o que mostra: o texto vem de um processo
/// externo, e uma versão futura, um erro de biblioteca ou um arquivo trocado não podem fazer um
/// segredo aparecer numa janela que o operador fotografa e manda por WhatsApp. Segunda camada,
/// barata, que só tira o que não deveria estar ali.
/// </summary>
public static class SaidaProcessoSeguraHelper
{
    public const int MaximoCaracteresPorLinha = 400;

    private static readonly Regex SequenciaAnsi = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CaracteresDeControle = new(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UrlDeBanco = new(@"postgres(?:ql)?://\S+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex StringDeConexao = new(@"\b(?:host|server|data source)\s*=\s*[^;\s]+;[^\r\n]*", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex AtribuicaoSecreta = new(
        @"\b([A-Za-z0-9_.-]*(?:password|pwd|passwd|senha|secret|segredo|token|api[_-]?key|authorization)[A-Za-z0-9_.-]*)\s*[=:]\s*(?:(?:bearer|basic)\s+)?(""[^""]*""|'[^']*'|\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Portador = new(@"\bbearer\s+\S+", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex SequenciaHexadecimal = new(@"\b[0-9a-fA-F]{32,}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CnpjFormatado = new(@"\b\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CpfFormatado = new(@"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Cnpj14Digitos = new(@"\b\d{14}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Cpf11Digitos = new(@"\b\d{11}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A linha pronta para exibir, ou <c>null</c> quando sobra nada (linha em branco).</summary>
    public static string? Sanitizar(string? linha, int maximoCaracteres = MaximoCaracteresPorLinha)
    {
        if (string.IsNullOrEmpty(linha))
            return null;

        var texto = SequenciaAnsi.Replace(linha, string.Empty);
        texto = CaracteresDeControle.Replace(texto, string.Empty);
        texto = UrlDeBanco.Replace(texto, "[conexão oculta]");
        texto = StringDeConexao.Replace(texto, "[conexão oculta]");
        texto = AtribuicaoSecreta.Replace(texto, "$1=[oculto]");
        texto = Portador.Replace(texto, "Bearer [oculto]");
        texto = SequenciaHexadecimal.Replace(texto, "[oculto]");
        texto = CnpjFormatado.Replace(texto, "[CNPJ oculto]");
        texto = CpfFormatado.Replace(texto, "[CPF oculto]");
        texto = Cnpj14Digitos.Replace(texto, "[CNPJ oculto]");
        texto = Cpf11Digitos.Replace(texto, "[CPF oculto]");
        texto = Email.Replace(texto, "[e-mail oculto]");

        texto = texto.TrimEnd();
        if (texto.Length == 0)
            return null;

        return texto.Length > maximoCaracteres ? texto[..maximoCaracteres].TrimEnd() + "…" : texto;
    }
}
