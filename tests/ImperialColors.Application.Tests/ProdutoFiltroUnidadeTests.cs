using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Security;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Filtro por unidade de medida (BD, GL, LA...) da lista de estoque: o serviço limpa as siglas
/// antes de mandar ao banco, e o banco só devolve — e só conta — o que o filtro deixa passar.
/// </summary>
public class ProdutoFiltroUnidadeTests
{
    // ---------- Serviço (repositório falso) ----------

    private static (ProdutoService Servico, Mock<IProdutoRepository> Repositorio) Criar()
    {
        var repositorio = new Mock<IProdutoRepository>();
        repositorio
            .Setup(r => r.ObterPaginadoAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Produto>(), 0));

        var servico = new ProdutoService(
            repositorio.Object,
            Mock.Of<IRepository<Categoria>>(),
            Mock.Of<IRepository<Marca>>(),
            Mock.Of<ITributacaoProdutoRepository>(),
            Mock.Of<IConfiguracaoFiscalService>(),
            Mock.Of<IAuditoriaService>(),
            new UsuarioAtualSistema(),
            NullLogger<ProdutoService>.Instance);
        return (servico, repositorio);
    }

    private static IReadOnlyCollection<string>? UnidadesEnviadasAoBanco(Mock<IProdutoRepository> repositorio)
    {
        IReadOnlyCollection<string>? enviadas = null;
        foreach (var chamada in repositorio.Invocations.Where(i => i.Method.Name == nameof(IProdutoRepository.ObterPaginadoAsync)))
            enviadas = (IReadOnlyCollection<string>?)chamada.Arguments[4];
        return enviadas;
    }

    [Fact]
    public async Task SiglasChegamAoBancoEmMaiusculaESemRepeticao()
    {
        var (servico, repositorio) = Criar();

        await servico.ObterPaginadoAsync(1, 50, unidades: ["bd", " GL ", "BD"]);

        Assert.Equal(["BD", "GL"], UnidadesEnviadasAoBanco(repositorio)!.ToArray());
    }

    [Fact]
    public async Task SemSiglas_NaoFiltra()
    {
        var (servico, repositorio) = Criar();

        await servico.ObterPaginadoAsync(1, 50, unidades: []);

        Assert.Null(UnidadesEnviadasAoBanco(repositorio));
    }

    /// <summary>Sigla que não existe não pode virar "nenhum produto": a lista parecia estoque
    /// vazio. Sem nenhuma sigla válida, é como se não houvesse filtro.</summary>
    [Fact]
    public async Task SoSiglasInvalidas_ViramSemFiltro()
    {
        var (servico, repositorio) = Criar();

        await servico.ObterPaginadoAsync(1, 50, unidades: ["XYZ", "  "]);

        Assert.Null(UnidadesEnviadasAoBanco(repositorio));
    }

    [Fact]
    public async Task SiglaInvalidaMisturadaComValida_MantemSoAValida()
    {
        var (servico, repositorio) = Criar();

        await servico.ObterPaginadoAsync(1, 50, unidades: ["XYZ", "BD"]);

        Assert.Equal(["BD"], UnidadesEnviadasAoBanco(repositorio)!.ToArray());
    }

    // ---------- Banco de verdade ----------

    private static string Sufixo() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private sealed class Cenario : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required IDbContextFactory<AppDbContext> Fabrica { get; init; }
        public required string Marcador { get; init; }
        public IProdutoService Servico => Provider.GetRequiredService<IProdutoService>();

        public async ValueTask DisposeAsync()
        {
            await using var ctx = Fabrica.CreateDbContext();
            var padrao = $"%{Marcador}%";
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM produtos WHERE nome LIKE {padrao}");
            await Provider.DisposeAsync();
        }
    }

    /// <summary>Quatro produtos com o mesmo marcador no nome, em unidades diferentes: dois BD,
    /// um GL e um UN.</summary>
    private static async Task<Cenario?> CriarComProdutosAsync()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var cs))
            return null;

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(cs);
        services.AddApplication();
        var provider = services.BuildServiceProvider();
        var fabrica = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using var ctx = fabrica.CreateDbContext();
        await ctx.Database.MigrateAsync();

        var marcador = $"FU{Sufixo()}";
        foreach (var (codigo, unidade) in new[] { ("1", "BD"), ("2", "BD"), ("3", "GL"), ("4", "UN") })
        {
            ctx.Produtos.Add(new Produto
            {
                CodigoInterno = $"{marcador}-{codigo}",
                Nome = $"{marcador} Tinta {unidade} {codigo}",
                Unidade = unidade,
                QuantidadeEstoque = 1m,
                EstoqueMinimo = 0m,
                PrecoVenda = 10m
            });
        }
        await ctx.SaveChangesAsync();

        return new Cenario { Provider = provider, Fabrica = fabrica, Marcador = marcador };
    }

    [Fact]
    public async Task Banco_FiltroDeUmaUnidade_DevolveSoEla()
    {
        await using var cenario = await CriarComProdutosAsync();
        if (cenario is null) return;

        var pagina = await cenario.Servico.ObterPaginadoAsync(1, 50, cenario.Marcador, unidades: ["BD"]);

        Assert.Equal(2, pagina.TotalItens);
        Assert.All(pagina.Itens, p => Assert.Equal("BD", p.Unidade));
    }

    [Fact]
    public async Task Banco_FiltroDeVariasUnidades_SomaAsDuas()
    {
        await using var cenario = await CriarComProdutosAsync();
        if (cenario is null) return;

        var pagina = await cenario.Servico.ObterPaginadoAsync(1, 50, cenario.Marcador, unidades: ["BD", "GL"]);

        Assert.Equal(3, pagina.TotalItens);
        Assert.DoesNotContain(pagina.Itens, p => p.Unidade == "UN");
    }

    [Fact]
    public async Task Banco_SemFiltro_DevolveTodas()
    {
        await using var cenario = await CriarComProdutosAsync();
        if (cenario is null) return;

        var pagina = await cenario.Servico.ObterPaginadoAsync(1, 50, cenario.Marcador);

        Assert.Equal(4, pagina.TotalItens);
    }

    /// <summary>O filtro combina com a busca: pesquisar "GL" dentro de "só BD" não acha nada, em
    /// vez de furar o filtro. É o que o operador espera de um filtro persistente.</summary>
    [Fact]
    public async Task Banco_FiltroCombinaComABusca_NaoFuraOFiltro()
    {
        await using var cenario = await CriarComProdutosAsync();
        if (cenario is null) return;

        var comBd = await cenario.Servico.ObterPaginadoAsync(1, 50, $"{cenario.Marcador} Tinta BD 1", unidades: ["BD"]);
        var glDentroDeBd = await cenario.Servico.ObterPaginadoAsync(1, 50, $"{cenario.Marcador} Tinta GL", unidades: ["BD"]);

        Assert.Equal(1, comBd.TotalItens);
        Assert.Equal(0, glDentroDeBd.TotalItens);
    }

    /// <summary>A paginação conta só o que o filtro deixa passar: o total e as páginas não podem
    /// vir do estoque inteiro.</summary>
    [Fact]
    public async Task Banco_PaginacaoConsideraOFiltro()
    {
        await using var cenario = await CriarComProdutosAsync();
        if (cenario is null) return;

        var primeira = await cenario.Servico.ObterPaginadoAsync(1, 1, cenario.Marcador, unidades: ["BD"]);
        var segunda = await cenario.Servico.ObterPaginadoAsync(2, 1, cenario.Marcador, unidades: ["BD"]);

        Assert.Equal(2, primeira.TotalItens);
        Assert.Equal(2, primeira.TotalPaginas);
        Assert.Single(primeira.Itens);
        Assert.Single(segunda.Itens);
        Assert.NotEqual(primeira.Itens[0].Id, segunda.Itens[0].Id);
    }
}
