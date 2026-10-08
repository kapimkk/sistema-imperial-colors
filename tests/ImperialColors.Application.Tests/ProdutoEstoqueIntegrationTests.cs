using ImperialColors.Application.DTOs;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Homologação do módulo de estoque: busca paginada, buscas alternadas e hard delete.
/// Requer RUN_INTEGRATION_TESTS=true e PostgreSQL acessível via .env.
/// </summary>
public class ProdutoEstoqueIntegrationTests
{
    [Fact]
    public async Task Estoque_BuscaAlternadaEExclusao_DevePersistirSemErroDeContexto()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var cs))
            return;

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(cs);
        services.AddApplication();

        await using var provider = services.BuildServiceProvider();
        var produtoService = provider.GetRequiredService<IProdutoService>();
        var contextFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var categoriaRepo = provider.GetRequiredService<IRepository<Domain.Entities.Categoria>>();
        var marcaRepo = provider.GetRequiredService<IRepository<Domain.Entities.Marca>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var termoBase = $"Homolog{sufixo}";
        var idsCriados = new List<int>();
        Domain.Entities.Categoria? categoria = null;
        Domain.Entities.Marca? marca = null;

        try
        {
            categoria = await categoriaRepo.AdicionarAsync(new Domain.Entities.Categoria
            {
                Nome = $"Cat Homolog {sufixo}",
                Ativo = true
            });

            marca = await marcaRepo.AdicionarAsync(new Domain.Entities.Marca
            {
                Nome = $"Marca Homolog {sufixo}",
                Ativo = true
            });

            var nomes = new[]
            {
                $"{termoBase} Alpha Tinta",
                $"{termoBase} Beta Verniz",
                $"{termoBase} Gamma Esmalte"
            };

            foreach (var nome in nomes)
            {
                var codigo = await produtoService.GerarProximoCodigoInternoAsync();
                var criado = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
                    CodigoInterno = codigo,
                    Nome = nome,
                    CategoriaId = categoria.Id,
                    MarcaId = marca.Id,
                    Custo = 15m,
                    PrecoVenda = 29.90m,
                    QuantidadeEstoque = 10,
                    EstoqueMinimo = 2,
                    Unidade = "UN"
                });
                idsCriados.Add(criado.Id);
            }

            var termosBusca = new[] { termoBase, "Alpha", "Beta", "Gamma", termoBase, "Alpha" };
            foreach (var termo in termosBusca)
            {
                var pagina = await produtoService.ObterPaginadoAsync(1, 50, termo);
                Assert.Contains(pagina.Itens, p => p.Nome.Contains(termoBase, StringComparison.OrdinalIgnoreCase));
            }

            var idExcluir = idsCriados[0];
            await produtoService.RemoverAsync(idExcluir);

            var aposExclusao = await produtoService.ObterPaginadoAsync(1, 50, termoBase);
            Assert.DoesNotContain(aposExclusao.Itens, p => p.Id == idExcluir);
            Assert.Equal(2, aposExclusao.Itens.Count(p => p.Nome.Contains(termoBase, StringComparison.OrdinalIgnoreCase)));

            await using var db = await contextFactory.CreateDbContextAsync();
            var existeNoBanco = await db.Produtos.IgnoreQueryFilters()
                .AnyAsync(p => p.Id == idExcluir);
            Assert.False(existeNoBanco);

            var obterPorId = await produtoService.ObterPorIdAsync(idExcluir);
            Assert.Null(obterPorId);
        }
        finally
        {
            foreach (var id in idsCriados.Where(id => id > 0))
            {
                try { await produtoService.RemoverAsync(id); }
                catch { /* produto já removido no teste */ }
            }

            if (categoria is not null)
            {
                try { await categoriaRepo.RemoverAsync(categoria.Id); }
                catch { /* ignore */ }
            }

            if (marca is not null)
            {
                try { await marcaRepo.RemoverAsync(marca.Id); }
                catch { /* ignore */ }
            }
        }
    }
}
