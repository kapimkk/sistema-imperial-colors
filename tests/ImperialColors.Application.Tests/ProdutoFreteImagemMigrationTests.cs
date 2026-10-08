using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Repositories;
using ImperialColors.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace ImperialColors.Application.Tests;

public sealed class LocalCatalogFactAttribute : FactAttribute
{
    public LocalCatalogFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CATALOG_STORE_TEST_CONNECTION_STRING")))
            Skip = "Requer PostgreSQL local isolado e CATALOG_STORE_TEST_CONNECTION_STRING explícita.";
    }
}

public sealed class ProdutoFreteImagemMigrationTests
{
    [LocalCatalogFact]
    public async Task MigrationPreservaDuzentosLegadosECRUDComImagem()
    {
        var baseConnection = Environment.GetEnvironmentVariable("CATALOG_STORE_TEST_CONNECTION_STRING")!;
        var builder = new NpgsqlConnectionStringBuilder(baseConnection);
        if (builder.Host is not ("localhost" or "127.0.0.1") || builder.Port != 5438
            || builder.Database != "imperial_csharp_v3_test")
            throw new InvalidOperationException("A prova de migration exige fixture loopback:5438/imperial_csharp_v3_test.");
        builder.Database = $"imperial_csharp_v3_{Guid.NewGuid():N}_test";
        var dbName = builder.Database;
        var admin = new NpgsqlConnectionStringBuilder(baseConnection) { Database = "postgres" };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", connection);
            await create.ExecuteNonQueryAsync();
        }
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ConnectionString).Options;
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        await using var context = new AppDbContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260928202838_AddComissaoItemVendaExterna");
        await context.Database.ExecuteSqlRawAsync("""
            INSERT INTO produtos (codigo_interno,nome,quantidade_estoque,estoque_minimo,unidade,preco_venda,promocao_ativa,observacoes,criado_em,ativo)
            SELECT 'LEGACY'||i,'Produto legado de teste '||i,12.500,0,'UN',50,false,'Linha 1'||chr(10)||'Linha 2',NOW(),true
            FROM generate_series(1,222) i;
            """);
        await migrator.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        var legados = await context.Produtos.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        Assert.Equal(222, legados.Count);
        Assert.All(legados, produto =>
        {
            Assert.Null(produto.PesoGramas);
            Assert.Null(produto.AlturaCm);
            Assert.Null(produto.LarguraCm);
            Assert.Null(produto.ComprimentoCm);
            Assert.Null(produto.ImagemProdutoPath);
            Assert.False(produto.ImagemRemovida);
            Assert.Equal("Linha 1\nLinha 2", produto.Observacoes);
            Assert.Equal(12.500m, produto.QuantidadeEstoque);
        });
        var factory = new Factory(options);
        var repository = new ProdutoRepository(factory);
        var categoria = new Categoria {Nome = "Categoria exclusivamente local"};
        var marca = new Marca {Nome = "Marca exclusivamente local"};
        context.Categorias.Add(categoria);
        context.Marcas.Add(marca);
        await context.SaveChangesAsync();
        var categorias = new CategoriaRepository(factory);
        var marcas = new MarcaRepository(factory);
        var pasta = Path.Combine(Path.GetTempPath(), $"imperial_catalog_image_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(pasta);
        var storage = new ImagemProdutoStorage(pasta);
        var source = Path.Combine(pasta, "produto.png");
        using (var bitmap = new System.Drawing.Bitmap(2, 2))
            bitmap.Save(source, System.Drawing.Imaging.ImageFormat.Png);
        try
        {
            var service = new ProdutoService(repository,categorias,marcas,Mock.Of<ITributacaoProdutoRepository>(),
                Mock.Of<IConfiguracaoFiscalService>(),Mock.Of<IAuditoriaService>(),
                new UsuarioAtualSistema(),NullLogger<ProdutoService>.Instance,storage);
            // Edição real de legado continua possível sem inventar medidas ou perder dados.
            var legado = legados.Single(produto => produto.CodigoInterno == "LEGACY1");
            var legadoSalvo = await service.AtualizarAsync(legado.Id, new AtualizarProdutoDto
            {
                CodigoInterno = legado.CodigoInterno, Nome = legado.Nome,
                CategoriaId = categoria.Id, MarcaId = marca.Id, PrecoVenda = legado.PrecoVenda,
                QuantidadeEstoque = legado.QuantidadeEstoque, QuantidadeEstoqueOriginal = legado.QuantidadeEstoque,
                EstoqueMinimo = legado.EstoqueMinimo, Unidade = legado.Unidade, Observacoes = legado.Observacoes
            });
            Assert.Equal(legado.CodigoInterno, legadoSalvo.CodigoInterno);
            Assert.Equal(12.500m, legadoSalvo.QuantidadeEstoque);
            Assert.Equal("Linha 1\nLinha 2", legadoSalvo.Observacoes);
            Assert.Null(legadoSalvo.PesoGramas);
            Assert.Null(legadoSalvo.AlturaCm);
            Assert.Null(legadoSalvo.LarguraCm);
            Assert.Null(legadoSalvo.ComprimentoCm);
            var legadoPersistido = await repository.ObterPorIdAsync(legado.Id);
            Assert.Equal(legadoSalvo.Observacoes, legadoPersistido!.Observacoes);
            Assert.Null(legadoPersistido.PesoGramas);
            Assert.Null(legadoPersistido.AlturaCm);
            Assert.Null(legadoPersistido.LarguraCm);
            Assert.Null(legadoPersistido.ComprimentoCm);
            var novo = await service.CriarAsync(new CriarProdutoDto
            {
                CodigoInterno = "FRETE001", CodigoInternoDefinidoManualmente = true, Nome = "Produto local",
                CategoriaId = categoria.Id, MarcaId = marca.Id, PrecoVenda = 100m, QuantidadeEstoque = 5,
                PesoGramas = 5500, AlturaCm = 25.25m, LarguraCm = 20.10m, ComprimentoCm = 30.99m,
                Observacoes = "Descrição completa\nSegunda linha", AlterarImagem = true, ImagemArquivoSelecionado = source
            });
            Assert.True(File.Exists(storage.ObterCaminhoSeguro(novo.ImagemProdutoPath)));
            // B abriu edição sem modificar imagem; A substitui e confirma antes do save de B.
            var edicaoB = (await repository.ObterPorIdAsync(novo.Id))!;
            var substituidaA = await service.AtualizarAsync(novo.Id, new AtualizarProdutoDto
            {
                CodigoInterno = novo.CodigoInterno, Nome = novo.Nome, CategoriaId = categoria.Id, MarcaId = marca.Id,
                PrecoVenda = 100m, QuantidadeEstoque = 5, PesoGramas = 5500, AlturaCm = 25.25m,
                LarguraCm = 20.10m, ComprimentoCm = 30.99m, Observacoes = novo.Observacoes,
                AlterarImagem = true, ImagemArquivoSelecionado = source
            });
            Assert.NotEqual(novo.ImagemProdutoPath, substituidaA.ImagemProdutoPath);
            Assert.False(File.Exists(storage.ObterCaminhoSeguro(novo.ImagemProdutoPath)));
            edicaoB.Observacoes = "Descrição editada na tela B";
            var salvoB = await repository.AtualizarComAjusteEstoqueTransacionalAsync(edicaoB,5,5,"Edição B","Teste");
            Assert.Equal(substituidaA.ImagemProdutoPath,salvoB.ImagemProdutoPath);
            Assert.True(File.Exists(storage.ObterCaminhoSeguro(salvoB.ImagemProdutoPath)));
            novo.ImagemProdutoPath = substituidaA.ImagemProdutoPath;
            // Imagem preparada não sobrevive a rollback real da transação de produto/estoque.
            var numeroArquivos = Directory.GetFiles(Path.Combine(pasta,ImagemProdutoStorage.Pasta)).Length;
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE produtos SET quantidade_estoque=0 WHERE id={novo.Id}");
            await Assert.ThrowsAsync<DomainException>(() => service.AtualizarAsync(novo.Id,new AtualizarProdutoDto
            {
                CodigoInterno = novo.CodigoInterno, Nome = novo.Nome, CategoriaId = categoria.Id, MarcaId = marca.Id,
                PrecoVenda = 100m, QuantidadeEstoque = 1, QuantidadeEstoqueOriginal = 5, PesoGramas = 5500,
                AlturaCm = 25.25m, LarguraCm = 20.10m, ComprimentoCm = 30.99m,
                Observacoes = novo.Observacoes, AlterarImagem = true, ImagemArquivoSelecionado = source
            }));
            var aposRollback = await repository.ObterPorIdAsync(novo.Id);
            Assert.Equal(novo.ImagemProdutoPath,aposRollback!.ImagemProdutoPath);
            Assert.Equal(0m,aposRollback.QuantidadeEstoque);
            Assert.Equal(numeroArquivos,Directory.GetFiles(Path.Combine(pasta,ImagemProdutoStorage.Pasta)).Length);
            Assert.True(File.Exists(storage.ObterCaminhoSeguro(aposRollback.ImagemProdutoPath)));
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE produtos SET quantidade_estoque=5 WHERE id={novo.Id}");
            var atualizado = await service.AtualizarAsync(novo.Id, new AtualizarProdutoDto
            {
                CodigoInterno = novo.CodigoInterno, Nome = novo.Nome, CategoriaId = categoria.Id, MarcaId = marca.Id,
                PrecoVenda = 100m, QuantidadeEstoque = 5, QuantidadeEstoqueOriginal = 5,
                PesoGramas = 6001, AlturaCm = 26.01m, LarguraCm = 20.10m, ComprimentoCm = 30.99m,
                Observacoes = novo.Observacoes
            });
            Assert.Equal(6001, atualizado.PesoGramas);
            Assert.Equal(26.01m, atualizado.AlturaCm);
            Assert.Equal(novo.ImagemProdutoPath, atualizado.ImagemProdutoPath);
            Assert.Equal(novo.Observacoes, atualizado.Observacoes);
            var semImagem = await service.AtualizarAsync(novo.Id, new AtualizarProdutoDto
            {
                CodigoInterno = novo.CodigoInterno, Nome = novo.Nome, CategoriaId = categoria.Id, MarcaId = marca.Id,
                PrecoVenda = 100m, QuantidadeEstoque = 5, PesoGramas = 6001, AlturaCm = 26.01m,
                LarguraCm = 20.10m, ComprimentoCm = 30.99m, Observacoes = novo.Observacoes, RemoverImagem = true
            });
            Assert.True(semImagem.ImagemRemovida);
            Assert.Null(semImagem.ImagemProdutoPath);
            Assert.False(File.Exists(storage.ObterCaminhoSeguro(novo.ImagemProdutoPath)));
            await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE produtos SET altura_cm=0 WHERE codigo_interno='LEGACY1'"));
            Assert.Equal(223, await context.Produtos.CountAsync());
        }
        finally
        {
            if (Path.GetFullPath(pasta).StartsWith(Path.Combine(Path.GetTempPath(),"imperial_catalog_image_test_"),StringComparison.OrdinalIgnoreCase)
                && (File.GetAttributes(pasta) & FileAttributes.ReparsePoint)==0)
                Directory.Delete(pasta,true);
        }
    }

    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}