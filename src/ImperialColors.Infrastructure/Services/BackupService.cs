using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Constants;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Configuration;
using ImperialColors.Infrastructure.Services.Backup;
using Microsoft.Extensions.Logging;

namespace ImperialColors.Infrastructure.Services;

public class BackupService : IBackupService
{
    private readonly IParametroSistemaRepository _parametroRepository;
    private readonly Func<BackupOptions> _obterOptions;
    private readonly ILogger<BackupService> _logger;
    private int _executando;

    public BackupService(
        IParametroSistemaRepository parametroRepository,
        Func<BackupOptions> obterOptions,
        ILogger<BackupService> logger)
    {
        _parametroRepository = parametroRepository;
        _obterOptions = obterOptions;
        _logger = logger;
    }

    public void IniciarVerificacaoEmSegundoPlano()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await VerificarEExecutarSeNecessarioAsync();
            }
            catch (Exception ex)
            {
                BackupLogWriter.Registrar(_obterOptions().DiretorioRaiz, "Falha inesperada na rotina de backup.", ex);
                _logger.LogError(ex, "Falha inesperada na rotina de backup.");
            }
        });
    }

    internal async Task VerificarEExecutarSeNecessarioAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _executando, 1, 0) != 0)
            return;

        try
        {
            // Lida a cada verificação, nunca guardada em campo: se a pasta de backup foi
            // trocada pela tela de Configurações desde a última rodada, esta leitura já
            // enxerga o valor novo sem precisar reiniciar o sistema.
            var options = _obterOptions();

            var hoje = DateTime.Today;
            var ultimoBackup = await _parametroRepository.ObterDataAsync(
                ParametroSistemaChaves.DataUltimoBackup,
                cancellationToken);

            if (!BackupScheduleHelper.DeveExecutarBackup(ultimoBackup, hoje, options.IntervaloDias))
            {
                _logger.LogDebug("Backup automático não necessário. Último: {UltimoBackup}", ultimoBackup);
                return;
            }

            _logger.LogInformation("Iniciando backup híbrido automático.");

            try
            {
                await ExecutarBackupCompletoAsync(options, hoje, cancellationToken);
                await _parametroRepository.SalvarDataAsync(
                    ParametroSistemaChaves.DataUltimoBackup,
                    hoje,
                    cancellationToken);
                _logger.LogInformation("Backup automático concluído com sucesso.");
            }
            catch (Exception ex)
            {
                BackupLogWriter.Registrar(options.DiretorioRaiz, "Falha ao executar backup híbrido.", ex);
                _logger.LogError(ex, "Falha ao executar backup híbrido.");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _executando, 0);
        }
    }

    internal Task ExecutarBackupCompletoAsync(DateTime dataExecucao, CancellationToken cancellationToken = default)
        => ExecutarBackupCompletoAsync(_obterOptions(), dataExecucao, cancellationToken);

    private async Task ExecutarBackupCompletoAsync(BackupOptions options, DateTime dataExecucao, CancellationToken cancellationToken)
    {
        var pastaDestino = BackupPathHelper.MontarPastaDestino(options.DiretorioRaiz, dataExecucao);
        Directory.CreateDirectory(pastaDestino);

        var caminhoDump = Path.Combine(pastaDestino, BackupPathHelper.MontarNomeArquivoDump(options.PrefixoEmpresa, dataExecucao));
        var caminhoSql = Path.Combine(pastaDestino, BackupPathHelper.MontarNomeArquivoSql(options.PrefixoEmpresa, dataExecucao));

        var pgDump = PgDumpExecutor.LocalizarPgDump(options.PgDumpPath)
            ?? throw new InvalidOperationException(
                "Utilitário pg_dump não encontrado. Configure PG_DUMP_PATH no .env ou instale o PostgreSQL.");

        // Localizado ANTES de ler o banco: faltando o pg_restore, o backup falha de cara, em
        // vez de deixar na pasta um .dump sem o .sql e o dia marcado como "feito pela metade".
        var pgRestore = PgDumpExecutor.LocalizarPgRestore(pgDump)
            ?? throw new InvalidOperationException(
                $"Utilitário pg_restore não encontrado junto do pg_dump ({pgDump}). Reinstale o PostgreSQL ou ajuste PG_DUMP_PATH.");

        // Mesmo lock das edições de imagem em todos os PCs: dump e arquivos pertencem
        // ao mesmo estado, sem remoção/substituição entre o snapshot e a cópia.
        using var bloqueioImagens = await new ImagemProdutoStorage(options.RaizImagensProdutos)
            .AdquirirBloqueioAsync(cancellationToken);

        // Uma leitura do banco, dois arquivos — ver o sumário de PgDumpExecutor.
        await PgDumpExecutor.ExportarAsync(
            pgDump,
            options.Host,
            options.Porta,
            options.Usuario,
            options.Senha,
            options.Banco,
            caminhoDump,
            cancellationToken);

        await PgDumpExecutor.ConverterParaSqlAsync(pgRestore, caminhoDump, caminhoSql, cancellationToken);

        CopiarArquivosLocais(options, pastaDestino);
        await BackupImagensProdutos.CopiarAsync(options.RaizImagensProdutos, pastaDestino, cancellationToken);
    }

    private void CopiarArquivosLocais(BackupOptions options, string pastaDestino)
    {
        if (File.Exists(options.CaminhoAppsettings))
        {
            File.Copy(options.CaminhoAppsettings, Path.Combine(pastaDestino, "appsettings.json"), overwrite: true);
        }
        else
        {
            throw new FileNotFoundException("Arquivo appsettings.json não encontrado para backup.", options.CaminhoAppsettings);
        }

        if (!Directory.Exists(options.PastaLogos))
            throw new DirectoryNotFoundException($"Pasta de logos não encontrada: {options.PastaLogos}");

        var destinoLogos = Path.Combine(pastaDestino, "logos_empresa");
        // Sem icons/, ResolverPastaLogos usa a raiz da instalação. Nesse fallback não
        // percorremos subpastas de executáveis, logs, caches ou do próprio backup.
        var logosNaRaiz = string.Equals(Path.GetFullPath(options.PastaLogos).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(Path.GetDirectoryName(options.CaminhoAppsettings)!).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        CopiarDiretorio(options.PastaLogos, destinoLogos, recursivo: !logosNaRaiz);
    }

    private static readonly HashSet<string> ExtensoesLogo = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".ico", ".bmp", ".gif", ".webp" };

    private static void CopiarDiretorio(string origem, string destino, bool recursivo = true)
    {
        Directory.CreateDirectory(destino);

        foreach (var arquivo in Directory.GetFiles(origem))
        {
            var nome = Path.GetFileName(arquivo);
            // Somente assets de imagem: nunca .env, executáveis, logs ou configurações.
            if (!ExtensoesLogo.Contains(Path.GetExtension(nome))) continue;
            if ((File.GetAttributes(arquivo) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Um logo usa link externo; revise os assets antes do backup.");
            File.Copy(arquivo, Path.Combine(destino, nome), overwrite: true);
        }

        if (!recursivo) return;
        foreach (var subpasta in Directory.GetDirectories(origem))
        {
            var nome = Path.GetFileName(subpasta);
            // Catálogo tem pacote/manifesto próprios, não entra novamente em logos_empresa.
            if (nome.Equals(ImagemProdutoStorage.Pasta, StringComparison.OrdinalIgnoreCase)) continue;
            if ((File.GetAttributes(subpasta) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A pasta de logos usa link externo; revise os assets antes do backup.");
            CopiarDiretorio(subpasta, Path.Combine(destino, nome));
        }
    }
}
