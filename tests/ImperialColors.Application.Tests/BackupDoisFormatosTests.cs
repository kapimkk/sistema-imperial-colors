using System.Text;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Configuration;
using ImperialColors.Infrastructure.Services;
using ImperialColors.Infrastructure.Services.Backup;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Backup em dois formatos: o <c>.dump</c> sai do <c>pg_dump</c> e o <c>.sql</c> é convertido a
/// partir dele pelo <c>pg_restore</c>. O teste de integração gera um backup de verdade — é o
/// único jeito de provar que os dois arquivos saem e que cada um está no formato certo: o
/// <c>.dump</c> com a assinatura do formato custom, o <c>.sql</c> com o script do banco do
/// sistema (não só "um arquivo com extensão .sql").
/// </summary>
public class BackupDoisFormatosTests : IDisposable
{
    private readonly string _pastaTemp = Path.Combine(
        Path.GetTempPath(), "ImperialColorsBackupTests", Guid.NewGuid().ToString("N"));

    public BackupDoisFormatosTests() => Directory.CreateDirectory(_pastaTemp);

    public void Dispose()
    {
        try { Directory.Delete(_pastaTemp, recursive: true); } catch (IOException) { /* pasta temporária */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// O pg_restore tem que ser o que está na mesma pasta do pg_dump: os dois vêm da mesma
    /// instalação, e um pg_restore mais antigo achado em outro lugar do PATH recusaria o
    /// .dump por versão de arquivo não suportada.
    /// </summary>
    [Fact]
    public void LocalizarPgRestore_PrefereOQueEstaNaMesmaPastaDoPgDump()
    {
        var bin = Directory.CreateDirectory(Path.Combine(_pastaTemp, "PostgreSQL", "18", "bin")).FullName;
        var pgDump = Path.Combine(bin, "pg_dump.exe");
        var pgRestore = Path.Combine(bin, "pg_restore.exe");
        File.WriteAllText(pgDump, string.Empty);
        File.WriteAllText(pgRestore, string.Empty);

        Assert.Equal(pgRestore, PgDumpExecutor.LocalizarPgRestore(pgDump));
    }

    [Fact]
    public async Task BackupCompleto_GeraDumpComprimidoESqlConvertidoDele()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var conexaoTeste))
            return;

        // Somente conexão local explícita validada pelo guard; nunca usa .env da instalação.
        var bancoTeste = new Npgsql.NpgsqlConnectionStringBuilder(conexaoTeste);
        var pgDump = PgDumpExecutor.LocalizarPgDump(Environment.GetEnvironmentVariable("PG_DUMP_PATH"));
        if (pgDump is null)
            return; // sem PostgreSQL instalado nesta máquina, não há o que testar

        var pastaLogos = Directory.CreateDirectory(Path.Combine(_pastaTemp, "icons")).FullName;
        File.WriteAllText(Path.Combine(pastaLogos, "logo.png"), "logo");
        var appsettings = Path.Combine(_pastaTemp, "appsettings.json");
        File.WriteAllText(appsettings, "{}");

        var destino = Path.Combine(_pastaTemp, "backups");
        var options = new BackupOptions
        {
            DiretorioRaiz = destino,
            PrefixoEmpresa = "teste",
            Host = bancoTeste.Host,
            Porta = bancoTeste.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Banco = bancoTeste.Database!,
            Usuario = bancoTeste.Username!,
            Senha = bancoTeste.Password!,
            PgDumpPath = pgDump,
            PastaLogos = pastaLogos,
            CaminhoAppsettings = appsettings
        };

        var servico = new BackupService(
            Mock.Of<IParametroSistemaRepository>(),
            () => options,
            NullLogger<BackupService>.Instance);

        var data = new DateTime(2026, 9, 22);
        await servico.ExecutarBackupCompletoAsync(data);

        var pastaDoDia = Path.Combine(destino, "setembro-2026", "22-09-2026");
        var dump = Path.Combine(pastaDoDia, "backup_teste_22_09_2026.dump");
        var sql = Path.Combine(pastaDoDia, "backup_teste_22_09_2026.sql");

        Assert.True(File.Exists(dump), ".dump não foi gerado");
        Assert.True(File.Exists(sql), ".sql não foi gerado");

        // Formato custom do pg_dump começa com a assinatura "PGDMP" — é isso que o pg_restore
        // reconhece. Um .dump que fosse texto puro com a extensão trocada não passaria aqui.
        var cabecalho = new byte[5];
        await using (var arquivo = File.OpenRead(dump))
            _ = await arquivo.ReadAsync(cabecalho);
        Assert.Equal("PGDMP", Encoding.ASCII.GetString(cabecalho));

        // O .sql tem que ser um script restaurável do banco do sistema, não só um arquivo
        // com a extensão certa.
        var script = await File.ReadAllTextAsync(sql);
        Assert.Contains("CREATE TABLE", script);
        Assert.Contains("produtos", script);
        Assert.Contains("notas_fiscais", script);
        // --no-owner: restaurar num PostgreSQL de outra máquina não pode depender de o usuário
        // dono original existir lá.
        Assert.DoesNotContain("OWNER TO", script);

        // O .dump comprimido fica menor que o script em texto — é o motivo de existir o par.
        Assert.True(new FileInfo(dump).Length < new FileInfo(sql).Length,
            $".dump ({new FileInfo(dump).Length} bytes) deveria ser menor que o .sql ({new FileInfo(sql).Length} bytes)");

        // Os arquivos locais continuam indo junto, como antes.
        Assert.True(File.Exists(Path.Combine(pastaDoDia, "appsettings.json")));
        Assert.True(File.Exists(Path.Combine(pastaDoDia, "logos_empresa", "logo.png")));
    }
}
