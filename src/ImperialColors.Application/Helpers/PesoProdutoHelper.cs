using System.Globalization;

namespace ImperialColors.Application.Helpers;

/// <summary>
/// Conversão e exibição do peso do produto, guardado em gramas
/// (<see cref="Domain.Entities.Produto.PesoGramas"/>).
///
/// A tela aceita kg decimais, mas a persistência mantém gramas inteiros. A leitura não
/// arredonda pesos subgrama nem aceita separador de milhares ambíguo.
/// /// </summary>
public static class PesoProdutoHelper
{
    /// <summary>Cultura fixa em vez de <c>CurrentCulture</c>: "5,5 kg" com vírgula decimal
    /// faz parte do texto que o operador lê, e o app só existe em pt-BR. Depender da
    /// cultura do processo faria a mesma chamada devolver "5.5 kg" num ambiente sem a
    /// configuração de thread da UI aplicada — em teste automatizado, por exemplo.</summary>
    private static readonly CultureInfo CulturaPtBr = new("pt-BR");

    public const int GramasPorQuilo = 1000;

    /// <summary>Não arredonda gramas: até três casas decimais em kg, sem separador de milhares.</summary>
    public static bool TentarLerQuilos(string? texto, out int? gramas)
    {
        gramas = null;
        if (string.IsNullOrWhiteSpace(texto)) return true;
        var normalizado = texto.Trim().Replace(',', '.');
        if (!decimal.TryParse(normalizado, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kg)
            || kg <= 0 || kg > int.MaxValue / (decimal)GramasPorQuilo
            || decimal.Truncate(kg * GramasPorQuilo) != kg * GramasPorQuilo)
            return false;
        gramas = checked((int)(kg * GramasPorQuilo));
        return true;
    }

    public static bool TentarLerCentimetros(string? texto, out decimal? centimetros)
    {
        centimetros = null;
        if (string.IsNullOrWhiteSpace(texto)) return true;
        if (!decimal.TryParse(texto.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var valor)
            || valor <= 0 || valor >= 100_000_000m || decimal.Round(valor, 2) != valor)
            return false;
        centimetros = valor;
        return true;
    }

    /// <summary>Limite técnico de armazenamento em gramas (int32), não limite da Frenet/transportadoras.</summary>
    public const int PesoMaximoGramas = int.MaxValue;
    /// <summary>Peso em quilos, como a NF-e espera (tags <c>pesoB</c>/<c>pesoL</c>).</summary>
    public static decimal? EmQuilos(int? gramas)
        => gramas.HasValue ? gramas.Value / (decimal)GramasPorQuilo : null;

    /// <summary>
    /// Peso para leitura humana: abaixo de um quilo fica em gramas ("800 g"), daí para
    /// cima vira quilos ("5,5 kg"). Mostrar "0,8 kg" para um pote de 800 g só faz o
    /// operador conferir duas vezes se digitou certo.
    /// </summary>
    public static string Formatar(int? gramas)
    {
        if (gramas is not > 0)
            return string.Empty;

        if (gramas.Value < GramasPorQuilo)
            return $"{gramas.Value} g";

        var quilos = EmQuilos(gramas)!.Value;
        return $"{quilos.ToString("0.###", CulturaPtBr)} kg";
    }
}
