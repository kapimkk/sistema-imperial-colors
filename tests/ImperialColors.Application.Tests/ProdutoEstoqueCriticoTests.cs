using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImperialColors.Application.Tests;

public class ProdutoEstoqueCriticoTests
{
    private sealed class Fabrica(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task Contagem_IncluiAteDuasUnidadesSemMisturarZeradosOuInativos()
    {
        await using var conexao = new SqliteConnection("Data Source=:memory:");
        await conexao.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conexao).Options;

        // A contagem consulta só saldo e Ativo. O banco isolado não precisa das tabelas
        // comerciais/fiscais ou de uma conexão com o PostgreSQL da loja.
        await using (var contexto = new AppDbContext(options))
        {
            await contexto.Database.ExecuteSqlRawAsync("""
                CREATE TABLE produtos (
                    id INTEGER PRIMARY KEY,
                    quantidade_estoque NUMERIC NOT NULL,
                    ativo INTEGER NOT NULL
                );
                INSERT INTO produtos (id, quantidade_estoque, ativo) VALUES
                    (1, -1, 1), (2, 0, 1), (3, 0.25, 1), (4, 1, 1), (5, 2, 1),
                    (6, 2.001, 1), (7, 3, 1), (8, 5, 1), (9, 1, 0);
                """);
        }

        var repositorio = new ProdutoRepository(new Fabrica(options));
        IProdutoRepository contrato = repositorio;

        Assert.Equal(3, await contrato.ContarComEstoqueCriticoAsync());
        Assert.Equal(3, await repositorio.ContarComEstoqueCriticoAsync());
        Assert.Equal(2, await repositorio.ContarSemEstoqueAsync());
    }
}
