namespace ImperialColors.Infrastructure.Configuration;

public class BackupOptions
{
    public const string DiretorioRaizPadrao = @"C:\backup_sistema";
    public const string PrefixoEmpresaPadrao = "imperial";

    public string DiretorioRaiz { get; init; } = DiretorioRaizPadrao;
    public string PrefixoEmpresa { get; init; } = PrefixoEmpresaPadrao;
    public int IntervaloDias { get; init; } = 7;

    public string Host { get; init; } = "localhost";
    public string Porta { get; init; } = "5432";
    public string Banco { get; init; } = "imperial_colors";
    public string Usuario { get; init; } = "postgres";
    public string Senha { get; init; } = string.Empty;
    public string? PgDumpPath { get; init; }

    public string RaizImagensProdutos { get; init; } = AppContext.BaseDirectory;
    public string PastaLogos { get; init; } = string.Empty;
    public string CaminhoAppsettings { get; init; } = string.Empty;

    public static BackupOptions CarregarDoAmbiente()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var pastaLogos = ResolverPastaLogos(baseDir);
        var appsettings = Path.Combine(baseDir, "appsettings.json");

        return new BackupOptions
        {
            DiretorioRaiz = ObterEnv("BACKUP_PATH", DiretorioRaizPadrao),
            PrefixoEmpresa = ObterEnv("BACKUP_PREFIXO_EMPRESA", PrefixoEmpresaPadrao),
            IntervaloDias = int.TryParse(ObterEnv("BACKUP_INTERVALO_DIAS", "7"), out var dias) && dias > 0 ? dias : 7,
            Host = ObterEnv("DB_HOST", "localhost"),
            Porta = ObterEnv("DB_PORT", "5432"),
            Banco = ObterEnv("DB_NAME", "imperial_colors"),
            Usuario = ObterEnv("DB_USER", "postgres"),
            Senha = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? string.Empty,
            PgDumpPath = ObterEnvOpcional("PG_DUMP_PATH"),
            RaizImagensProdutos = Services.ImagemProdutoStorage.ResolverRaiz(baseDir,
                Environment.GetEnvironmentVariable("PRODUCT_IMAGES_ROOT")),
            PastaLogos = pastaLogos,
            CaminhoAppsettings = appsettings
        };
    }

    private static string ObterEnv(string chave, string padrao)
        => Environment.GetEnvironmentVariable(chave)?.Trim() is { Length: > 0 } valor ? valor : padrao;

    private static string? ObterEnvOpcional(string chave)
        => Environment.GetEnvironmentVariable(chave)?.Trim() is { Length: > 0 } valor ? valor : null;

    private static string ResolverPastaLogos(string baseDir)
    {
        var logoRelativo = ObterEnv("LOGO_PATH", "icons/logoimperialcolors.png");
        var candidatos = new[]
        {
            Path.IsPathRooted(logoRelativo) ? logoRelativo : Path.Combine(baseDir, logoRelativo),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", logoRelativo))
        };

        foreach (var caminhoArquivo in candidatos)
        {
            if (File.Exists(caminhoArquivo))
                return Path.GetDirectoryName(caminhoArquivo)!;
        }

        var pastaIcons = Path.Combine(baseDir, "icons");
        return Directory.Exists(pastaIcons) ? pastaIcons : baseDir;
    }
}
