using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Configuration;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Services;
using ImperialColors.Infrastructure.Services.Backup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace ImperialColors.Application.Tests;

public sealed class ProdutoImagensCompartilhadasBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"imperial_shared_images_test_{Guid.NewGuid():N}");
    public ProdutoImagensCompartilhadasBackupTests() => Directory.CreateDirectory(_root);
    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
    private string Png()
    {
        var path = Path.Combine(_root, "source.png");
        using var bitmap = new Bitmap(3, 2);
        bitmap.SetPixel(1, 1, Color.Gold);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public async Task DoisPcsImportamELeemMesmaReferenciaSemCopiasLocais()
    {
        var pcA = Folder("PC-A"); var pcB = Folder("PC-B"); var shared = Folder("catalogo-compartilhado");
        var a = new ImagemProdutoStorage(ImagemProdutoStorage.ResolverRaiz(pcA, shared));
        var b = new ImagemProdutoStorage(ImagemProdutoStorage.ResolverRaiz(pcB, shared));
        var reference = await a.ImportarAsync(Png());
        Assert.Equal(a.ObterCaminhoSeguro(reference), b.ObterCaminhoSeguro(reference));
        b.ValidarArquivo(b.ObterCaminhoSeguro(reference)!);
        Assert.False(Directory.Exists(Path.Combine(pcA, "ImagensProdutos")));
        Assert.False(Directory.Exists(Path.Combine(pcB, "ImagensProdutos")));
    }

    [Fact]
    public void ConfiguracaoIndisponivelNaoCriaFallbackLocal()
    {
        var local = Folder("instalacao");
        Assert.Throws<DomainException>(() => ImagemProdutoStorage.ResolverRaiz(local, Path.Combine(_root, "ausente")));
        Assert.False(Directory.Exists(Path.Combine(local, "ImagensProdutos")));
    }

    [Fact]
    public void ConfiguracaoRelativaRecusada()
        => Assert.Throws<DomainException>(() => ImagemProdutoStorage.ResolverRaiz(_root, "../outra"));

    [Fact]
    public async Task LockCompartilhadoSerializaBackupEdicaoELiberaAoFechar()
    {
        var root = Folder("share");
        var a = new ImagemProdutoStorage(root); var b = new ImagemProdutoStorage(root);
        using var held = await a.AdquirirBloqueioAsync();
        using var cts = new CancellationTokenSource(250);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.AdquirirBloqueioAsync(cts.Token));
        held.Dispose();
        using var acquired = await b.AdquirirBloqueioAsync();
        Assert.NotNull(acquired);
    }

    [Fact]
    public async Task BackupRestauraReferenciaRelativaHashEDecodificacaoEmOutraRaiz()
    {
        var source = Folder("source"); var backup = Folder("backup"); var restored = Folder("restored");
        var storage = new ImagemProdutoStorage(source);
        var reference = await storage.ImportarAsync(Png());
        using (await storage.AdquirirBloqueioAsync()) await BackupImagensProdutos.CopiarAsync(source, backup);
        await BackupImagensProdutos.RestaurarAsync(backup, restored);
        var copied = new ImagemProdutoStorage(restored);
        copied.ValidarArquivo(copied.ObterCaminhoSeguro(reference)!);
        Assert.Equal(await File.ReadAllBytesAsync(storage.ObterCaminhoSeguro(reference)!),
            await File.ReadAllBytesAsync(copied.ObterCaminhoSeguro(reference)!));
        await Assert.ThrowsAsync<DomainException>(() => BackupImagensProdutos.RestaurarAsync(backup, restored));
    }

    [Fact]
    public async Task BackupCorrompidoFalhaAntesDeCopiar()
    {
        var source = Folder("source"); var backup = Folder("backup"); var restored = Folder("restored");
        var storage = new ImagemProdutoStorage(source);
        var reference = await storage.ImportarAsync(Png());
        using (await storage.AdquirirBloqueioAsync()) await BackupImagensProdutos.CopiarAsync(source, backup);
        var file = new ImagemProdutoStorage(backup).ObterCaminhoSeguro(reference)!;
        await File.AppendAllTextAsync(file, "corrupcao");
        await Assert.ThrowsAsync<DomainException>(() => BackupImagensProdutos.RestaurarAsync(backup, restored));
        Assert.False(Directory.Exists(Path.Combine(restored, "ImagensProdutos")));
    }

    [Fact]
    public async Task ManifestoTraversalNaoEscreveForaDoDestino()
    {
        var backup = Folder("backup"); var restored = Folder("restored");
        var manifest = new BackupImagensProdutos.Pacote(1, [new("../outside.png", 1, "hash")]);
        await File.WriteAllTextAsync(Path.Combine(backup, BackupImagensProdutos.Manifesto), JsonSerializer.Serialize(manifest));
        await Assert.ThrowsAsync<DomainException>(() => BackupImagensProdutos.RestaurarAsync(backup, restored));
        Assert.Empty(Directory.EnumerateFileSystemEntries(restored));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestauracaoCanceladaOuIoFalhaDuranteCopiaNaoPublicaParcialELimpaStaging(bool cancelar)
    {
        var source = Folder("source"); var backup = Folder("backup"); var restored = Folder("restored");
        var storage = new ImagemProdutoStorage(source);
        await storage.ImportarAsync(Png()); await storage.ImportarAsync(Png());
        using (await storage.AdquirirBloqueioAsync()) await BackupImagensProdutos.CopiarAsync(source, backup);
        using var cts = new CancellationTokenSource();
        var copies = 0;
        Task Copy(string origin, string target, CancellationToken token)
        {
            if (++copies == 2) throw new IOException("Falha de disco fictícia.");
            File.Copy(origin, target);
            if (cancelar) cts.Cancel();
            return Task.CompletedTask;
        }
        if (cancelar) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BackupImagensProdutos.RestaurarValidadoAsync(backup, restored, Copy, cts.Token));
        else await Assert.ThrowsAsync<IOException>(() => BackupImagensProdutos.RestaurarValidadoAsync(backup, restored, Copy, cts.Token));
        Assert.False(Directory.Exists(Path.Combine(restored, "ImagensProdutos")));
        Assert.Empty(Directory.EnumerateDirectories(restored, ".ImagensProdutos.restore-*"));
        await BackupImagensProdutos.RestaurarAsync(backup, restored);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(restored, "ImagensProdutos")).Length);
    }

    [LocalCatalogFact]
    public async Task BackupRealBancoEImagensRestauraEmBancoEDiretorioDescartaveis()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CATALOG_STORE_TEST_CONNECTION_STRING")!);
        if (connection.Host is not ("localhost" or "127.0.0.1") || connection.Port != 5438
            || connection.Database != "imperial_csharp_v3_test")
            throw new InvalidOperationException("Backup real exige fixture loopback:5438/imperial_csharp_v3_test.");
        var prefix = "imperial_image_backup_" + Guid.NewGuid().ToString("N");
        var sourceDb = prefix + "_test"; var restoredDb = prefix + "_restored_test";
        connection.Database = "postgres";
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        try
        {
            await new NpgsqlCommand($"CREATE DATABASE \"{sourceDb}\"", admin).ExecuteNonQueryAsync();
            await new NpgsqlCommand($"CREATE DATABASE \"{restoredDb}\"", admin).ExecuteNonQueryAsync();
            connection.Database = sourceDb;
            var source = Folder("source"); var restored = Folder("restored"); var backup = Folder("backup");
            var storage = new ImagemProdutoStorage(source);
            var reference = await storage.ImportarAsync(Png());
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options;
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            await using (var context = new AppDbContext(options))
            {
                await context.Database.MigrateAsync();
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO produtos (codigo_interno,nome,quantidade_estoque,estoque_minimo,unidade,preco_venda,promocao_ativa,observacoes,criado_em,ativo,peso_gramas,altura_cm,largura_cm,comprimento_cm,imagem_produto_path,imagem_removida)
                    VALUES ('BACKUP001','Produto de backup descartável',12.500,0,'UN',50,false,'Descrição preservada',NOW(),true,5500,25.25,20.10,30.99,{reference},false)
                    """);
            }
            // Sem icons/, BackupOptions usa a raiz: não copiar o lock aberto nem duplicar imagens em logos.
            var logo = source; File.Copy(Png(), Path.Combine(logo, "logo.png"));
            var config = Path.Combine(source, "appsettings.json"); await File.WriteAllTextAsync(config, "{}");
            await File.WriteAllTextAsync(Path.Combine(source, ".env"), "DB_PASSWORD=fixture-private-not-for-backup");
            await File.WriteAllTextAsync(Path.Combine(source, "ImperialSync.env"), "SYNC_AGENT_SECRET=fixture-private-not-for-backup");
            await File.WriteAllTextAsync(Path.Combine(source, "program.exe"), "fixture");
            await File.WriteAllTextAsync(Path.Combine(source, "app.log"), "fixture-private-not-for-backup");
            var nested = Directory.CreateDirectory(Path.Combine(source, "logs")).FullName;
            File.Copy(Png(), Path.Combine(nested, "debug-screenshot.png"));
            var pgDump = PgDumpExecutor.LocalizarPgDump(Environment.GetEnvironmentVariable("PG_DUMP_PATH"))
                ?? throw new InvalidOperationException("pg_dump não encontrado para a prova real.");
            var backupOptions = new BackupOptions
            {
                DiretorioRaiz = backup, PrefixoEmpresa = "catalogo", Host = connection.Host,
                Porta = connection.Port.ToString(), Usuario = connection.Username!, Senha = connection.Password!,
                Banco = sourceDb, PgDumpPath = pgDump, CaminhoAppsettings = config, PastaLogos = logo,
                RaizImagensProdutos = source
            };
            var service = new BackupService(Mock.Of<IParametroSistemaRepository>(), () => backupOptions, NullLogger<BackupService>.Instance);
            await service.ExecutarBackupCompletoAsync(new DateTime(2026, 10, 8));
            var day = Path.Combine(backup, "outubro-2026", "08-10-2026");
            var dump = Directory.GetFiles(day, "*.dump").Single();
            Assert.True(File.Exists(Path.ChangeExtension(dump, ".sql")));
            Assert.True(File.Exists(Path.Combine(day, "logos_empresa", "logo.png")));
            Assert.False(File.Exists(Path.Combine(day, "logos_empresa", ".imperial-catalog.lock")));
            Assert.False(Directory.Exists(Path.Combine(day, "logos_empresa", "ImagensProdutos")));
            Assert.Equal(new[] { "logo.png" }, Directory.GetFiles(Path.Combine(day, "logos_empresa")).Select(Path.GetFileName));
            Assert.Empty(Directory.GetDirectories(Path.Combine(day, "logos_empresa")));
            Assert.DoesNotContain(Directory.EnumerateFiles(day, "*", SearchOption.AllDirectories),
                path => Path.GetFileName(path) is ".env" or "ImperialSync.env" or "program.exe" or "app.log");
            var psi = new ProcessStartInfo(PgDumpExecutor.LocalizarPgRestore(pgDump)!)
            { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in new[] { "--no-owner", "--no-acl", "--exit-on-error", "-h", connection.Host, "-p", connection.Port.ToString(), "-U", connection.Username!, "-d", restoredDb, dump })
                psi.ArgumentList.Add(arg);
            psi.Environment["PGPASSWORD"] = connection.Password!;
            using (var process = Process.Start(psi)!)
            {
                var error = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                Assert.True(process.ExitCode == 0, "pg_restore recusou o pacote local: " + await error);
            }
            await BackupImagensProdutos.RestaurarAsync(day, restored);
            connection.Database = restoredDb;
            await using var restoredConnection = new NpgsqlConnection(connection.ConnectionString);
            await restoredConnection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT imagem_produto_path,peso_gramas,altura_cm,largura_cm,comprimento_cm,observacoes FROM produtos WHERE codigo_interno='BACKUP001'", restoredConnection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); Assert.Equal(reference, reader.GetString(0));
            Assert.Equal(5500, reader.GetInt32(1)); Assert.Equal(25.25m, reader.GetDecimal(2));
            Assert.Equal(20.10m, reader.GetDecimal(3)); Assert.Equal(30.99m, reader.GetDecimal(4));
            Assert.Equal("Descrição preservada", reader.GetString(5)); Assert.False(await reader.ReadAsync());
            var restoredFile = new ImagemProdutoStorage(restored).ObterCaminhoSeguro(reference)!;
            new ImagemProdutoStorage(restored).ValidarArquivo(restoredFile);
            Assert.Equal(SHA256.HashData(File.ReadAllBytes(storage.ObterCaminhoSeguro(reference)!)), SHA256.HashData(File.ReadAllBytes(restoredFile)));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{sourceDb}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{restoredDb}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
        }
    }

    public void Dispose()
    {
        var path = Path.GetFullPath(_root);
        if (path.StartsWith(Path.Combine(Path.GetTempPath(), "imperial_shared_images_test_"), StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            Directory.Delete(path, recursive: true);
    }
}
