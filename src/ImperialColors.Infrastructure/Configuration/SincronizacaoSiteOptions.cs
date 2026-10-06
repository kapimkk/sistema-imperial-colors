namespace ImperialColors.Infrastructure.Configuration;

/// <summary>
/// Como o sistema executa o ImperialSync.exe. Os valores padrão são os de produção; só os testes
/// trocam o executável (por um script) e o tempo máximo.
/// </summary>
public sealed class SincronizacaoSiteOptions
{
    public const string NomeExecutavel = "ImperialSync.exe";
    public const string NomeArquivoConfiguracao = "ImperialSync.env";

    /// <summary>A única forma como o sistema chama o ImperialSync: uma rodada completa e termina
    /// (vendas do site para a loja, depois estoque da loja para o site).</summary>
    public const string ArgumentoRodadaUnica = "--once";

    /// <summary>Onde o sistema procura o programa: na pasta do próprio executável do sistema
    /// (<c>AppContext.BaseDirectory</c>), ao lado do <c>ImperialSync.env</c>.</summary>
    public string CaminhoExecutavel { get; init; } = Path.Combine(AppContext.BaseDirectory, NomeExecutavel);

    /// <summary>Teto de uma rodada. Uma rodada normal leva segundos; com o site fora do ar o
    /// ImperialSync tenta algumas vezes antes de desistir, e isso precisa caber aqui.</summary>
    public TimeSpan TempoMaximo { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Quantas linhas de saída ficam guardadas para a tela (as últimas).</summary>
    public int MaximoLinhasSaida { get; init; } = 120;
}
