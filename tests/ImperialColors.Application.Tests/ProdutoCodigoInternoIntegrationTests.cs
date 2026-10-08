using ImperialColors.Application.DTOs;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Entities;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ImperialColors.Application.Tests;

public class ProdutoCodigoInternoIntegrationTests
{
    [Fact]
    public async Task CriarTresProdutosSeguidos_DeveGerarCodigosSequenciaisUnicos()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var cs))
            return;

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(cs);
        services.AddApplication();

        await using var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var produtoService = scope.ServiceProvider.GetRequiredService<IProdutoService>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var sufixo = Guid.NewGuid().ToString("N");
        var categoria = new Categoria { Nome = $"CodigoInternoCategoria-{sufixo}", Ativo = true };
        var marca = new Marca { Nome = $"CodigoInternoMarca-{sufixo}", Ativo = true };
        await using (var context = await contextFactory.CreateDbContextAsync())
        {
            // Fixtures próprias: nunca selecionar registros criados/removidos por outro teste.
            context.Categorias.Add(categoria);
            context.Marcas.Add(marca);
            await context.SaveChangesAsync();
        }


        var codigosGerados = new List<string>();
        var idsCriados = new List<int>();

        try
        {
            var maiorAntes = (await produtoService.ObterTodosAsync())
                .Select(p => p.CodigoInterno)
                .Where(c => c.StartsWith('P') && int.TryParse(c[1..], out _))
                .Select(c => int.Parse(c[1..]))
                .DefaultIfEmpty(0)
                .Max();

            for (var i = 0; i < 3; i++)
            {
                var codigoSugerido = await produtoService.GerarProximoCodigoInternoAsync();
                var criado = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
                    CodigoInterno = codigoSugerido,
                    CodigoInternoDefinidoManualmente = false,
                    Nome = $"Produto Stress {Guid.NewGuid():N}",
                    CategoriaId = categoria.Id,
                    MarcaId = marca.Id,
                    Custo = 10m,
                    PrecoVenda = 20m,
                    QuantidadeEstoque = 0,
                    EstoqueMinimo = 0,
                    Unidade = "UN"
                });

                codigosGerados.Add(criado.CodigoInterno);
                idsCriados.Add(criado.Id);
            }

            // O que este teste garante de verdade: três produtos criados em sequência recebem
            // códigos ÚNICOS. É a regra que importa (o índice único de codigo_interno depende
            // dela).
            Assert.Equal(3, codigosGerados.Distinct(StringComparer.OrdinalIgnoreCase).Count());

            // Dois formatos são legítimos, e o teste antes só aceitava o primeiro:
            //   • P##### — sequencial, o que GerarProximoCodigoInternoAsync sugere;
            //   • SIGLA### — derivado das iniciais do nome, que é o que
            //     InserirProdutoAsync produz ao REGENERAR o código em caso de colisão
            //     (GarantirCodigoInternoDisponivelAntesDeInserirAsync).
            // Com outros testes de integração criando produtos em paralelo, a colisão acontece
            // e a regeneração entra em ação — comportamento correto, mas que fazia este teste
            // falhar de forma intermitente, dependendo de o GUID do nome cair antes ou depois
            // de outra inserção.
            Assert.All(codigosGerados, c =>
                Assert.True(
                    ProdutoCodigoInternoHelper.EhCodigoSequencialPadrao(c) ||
                    ProdutoCodigoIniciaisHelper.EhCodigoPorIniciais(c),
                    $"Código '{c}' não segue nenhum dos dois formatos válidos (P##### ou SIGLA###)."));

            // A comparação com o maior sequencial anterior só faz sentido para os códigos no
            // formato sequencial — os por iniciais têm contador próprio, por sigla.
            Assert.All(
                codigosGerados.Where(ProdutoCodigoInternoHelper.EhCodigoSequencialPadrao),
                c => Assert.True(int.Parse(c[1..]) > maiorAntes || maiorAntes == 0));
        }
        finally
        {
            foreach (var id in idsCriados)
            {
                try { await produtoService.RemoverAsync(id); }
                catch { /* ignore */ }
            }
            await using var cleanup = await contextFactory.CreateDbContextAsync();
            await cleanup.Categorias.IgnoreQueryFilters().Where(c => c.Id == categoria.Id).ExecuteDeleteAsync();
            await cleanup.Marcas.IgnoreQueryFilters().Where(m => m.Id == marca.Id).ExecuteDeleteAsync();
        }
    }
}
