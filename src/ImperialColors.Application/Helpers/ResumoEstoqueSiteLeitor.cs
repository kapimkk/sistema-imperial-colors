using System.Globalization;
using System.Text.RegularExpressions;
using ImperialColors.Application.DTOs;

namespace ImperialColors.Application.Helpers;

/// <summary>
/// Lê, do que o ImperialSync escreveu, o resumo do estoque da rodada:
/// <code>
/// Recebidos: 222
/// Atualizados: 0
/// SKUs desconhecidos: 222
///   Exemplos (produtos da loja sem cadastro no site): 21201050, 301010001, 301010002
/// </code>
/// Essas quatro linhas são contrato com o ImperialSync (lá há testes que travam o texto). Este
/// sistema NÃO decide nada sobre o estoque com elas: só mostra os números ao operador e evita
/// chamar de "sucesso" uma rodada em que o site não reconheceu os produtos.
///
/// O código de saída continua sendo a fonte principal (14 e 15 são os alertas de produto sem
/// cadastro). A leitura do resumo é a segunda verificação — e a que traz os números. Se as linhas
/// não vierem, ou vierem em outro formato, o resultado é <c>null</c> e vale só o código de saída.
/// </summary>
public static class ResumoEstoqueSiteLeitor
{
    /// <summary>Quantos códigos sem cadastro a tela mostra, no máximo.</summary>
    public const int MaximoAmostra = 10;

    // No modo contínuo o ImperialSync põe a hora entre colchetes antes de cada linha. O botão usa
    // --once (sem hora), mas a leitura aceita as duas formas.
    private const string Prefixo = @"^\s*(?:\[[^\]]{1,40}\]\s*)?";

    private static readonly Regex Recebidos = Contador("Recebidos");
    private static readonly Regex Atualizados = Contador("Atualizados");
    private static readonly Regex Desconhecidos = Contador("SKUs desconhecidos");

    private static readonly Regex Exemplos = new(
        Prefixo + @"Exemplos \(produtos da loja sem cadastro no site\):\s*(?<lista>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // O mesmo alfabeto que o site aceita para o SKU. O que não couber nele — um código que o
    // filtro de saída mascarou, o pedaço final de uma linha cortada — não entra na amostra.
    private static readonly Regex Sku = new(
        @"^[A-Za-z0-9][A-Za-z0-9._\-/]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static Regex Contador(string rotulo) => new(
        Prefixo + Regex.Escape(rotulo) + @":\s*(?<n>\d{1,9})\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ResumoEstoqueSite? Ler(IEnumerable<string>? linhas)
    {
        if (linhas is null)
            return null;

        int? recebidos = null;
        int? atualizados = null;
        int? desconhecidos = null;
        IReadOnlyList<string> amostra = Array.Empty<string>();

        foreach (var linha in linhas)
        {
            if (string.IsNullOrEmpty(linha))
                continue;

            if (Numero(Recebidos, linha) is { } r)
            {
                // Cada resumo começa em "Recebidos": números de um resumo anterior não se misturam.
                recebidos = r;
                atualizados = null;
                desconhecidos = null;
                amostra = Array.Empty<string>();
            }
            else if (Numero(Atualizados, linha) is { } a)
            {
                atualizados = a;
            }
            else if (Numero(Desconhecidos, linha) is { } d)
            {
                desconhecidos = d;
            }
            else
            {
                var exemplos = Exemplos.Match(linha);
                if (exemplos.Success)
                    amostra = LerAmostra(exemplos.Groups["lista"].Value);
            }
        }

        // Resumo incompleto não é resumo: melhor não mostrar número nenhum do que um pela metade.
        if (recebidos is not > 0 || atualizados is null || desconhecidos is null)
            return null;

        return new ResumoEstoqueSite
        {
            Recebidos = recebidos.Value,
            Atualizados = atualizados.Value,
            SemCadastro = desconhecidos.Value,
            AmostraSemCadastro = desconhecidos.Value > 0 ? amostra : Array.Empty<string>()
        };
    }

    private static int? Numero(Regex padrao, string linha)
    {
        var match = padrao.Match(linha);
        return match.Success && int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : null;
    }

    private static IReadOnlyList<string> LerAmostra(string lista)
    {
        var texto = lista.TrimEnd();
        // Linha longa demais chega cortada (com reticências): o último código pode estar pela metade.
        var cortada = texto.EndsWith('…');
        var itens = texto.TrimEnd('…')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (cortada && itens.Count > 0)
            itens.RemoveAt(itens.Count - 1);

        return itens
            .Where(item => Sku.IsMatch(item))
            .Distinct(StringComparer.Ordinal)
            .Take(MaximoAmostra)
            .ToArray();
    }
}
