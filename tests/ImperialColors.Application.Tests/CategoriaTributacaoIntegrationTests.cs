using ImperialColors.Application.DTOs;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Perfil fiscal padrão por categoria: salvar, recuperar e herdar num produto novo.
/// Requer RUN_INTEGRATION_TESTS=true e PostgreSQL acessível via .env.
/// </summary>
[Collection(ConfiguracaoFiscalGlobalCollection.Nome)]
public class CategoriaTributacaoIntegrationTests
{
    private static bool TryCarregarConfig(out ServiceProvider provider)
    {
        provider = null!;
        if (!IntegrationTestGuard.TryObterConnectionString(out var cs))
            return false;

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(cs);
        services.AddApplication();

        provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        context.Database.Migrate();
        return true;
    }

    [Fact]
    public async Task SalvarTributacaoPadrao_DeveSerRecuperadoEHerdadoPeloProdutoNovo()
    {
        if (!TryCarregarConfig(out var provider))
            return;

        await using var scope = provider.CreateAsyncScope();
        var categoriaService = scope.ServiceProvider.GetRequiredService<ICategoriaService>();
        var produtoService = scope.ServiceProvider.GetRequiredService<IProdutoService>();
        var categoriaRepo = scope.ServiceProvider.GetRequiredService<IRepository<Categoria>>();
        var marcaRepo = scope.ServiceProvider.GetRequiredService<IRepository<Marca>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var categoria = await categoriaRepo.AdicionarAsync(new Categoria { Nome = $"CatTributacaoPadrao{sufixo}", Ativo = true });
        var marca = await marcaRepo.AdicionarAsync(new Marca { Nome = $"MarcaTributacaoPadrao{sufixo}", Ativo = true });

        // Antes de configurar, a categoria não tem padrão.
        var vazio = await categoriaService.ObterTributacaoPadraoAsync(categoria.Id);
        Assert.False(vazio.Preenchida);

        var perfil = new TributacaoCategoriaDto
        {
            Ncm = "32089000",
            CsosnIcms = "102", // Simples Nacional é o regime padrão quando não configurado
            AliquotaPis = 1.65m,
            AliquotaCofins = 7.6m
        };
        var salvo = await categoriaService.SalvarTributacaoPadraoAsync(categoria.Id, perfil);
        Assert.Equal("32089000", salvo.Ncm);
        Assert.Equal("102", salvo.CsosnIcms);

        var recuperado = await categoriaService.ObterTributacaoPadraoAsync(categoria.Id);
        Assert.True(recuperado.Preenchida);
        Assert.Equal("32089000", recuperado.Ncm);
        Assert.Equal(1.65m, recuperado.AliquotaPis);

        // Simula o que a tela faz ao herdar o padrão da categoria para um produto novo.
        var produto = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            CodigoInterno = await produtoService.GerarProximoCodigoInternoAsync(),
            Nome = $"ProdutoHerdaTributacao{sufixo}",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            Custo = 5m,
            PrecoVenda = 10m,
            QuantidadeEstoque = 0,
            EstoqueMinimo = 0,
            Unidade = "UN"
        });

        var tributacaoHerdada = recuperado.ParaProdutoDto(produto.Id);
        var salvoNoProduto = await produtoService.SalvarTributacaoAsync(produto.Id, tributacaoHerdada);
        Assert.Equal("32089000", salvoNoProduto.Ncm);
        Assert.Equal("102", salvoNoProduto.CsosnIcms);

        // Upsert: salvar de novo com outro NCM atualiza em vez de duplicar.
        perfil.Ncm = "32081010";
        var atualizado = await categoriaService.SalvarTributacaoPadraoAsync(categoria.Id, perfil);
        Assert.Equal("32081010", atualizado.Ncm);
    }

    [Fact]
    public async Task SalvarTributacaoPadrao_CsosnInvalidoParaSimplesNacional_LancaDomainException()
    {
        if (!TryCarregarConfig(out var provider))
            return;

        await using var scope = provider.CreateAsyncScope();
        var categoriaService = scope.ServiceProvider.GetRequiredService<ICategoriaService>();
        var categoriaRepo = scope.ServiceProvider.GetRequiredService<IRepository<Categoria>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var categoria = await categoriaRepo.AdicionarAsync(new Categoria { Nome = $"CatTributacaoInvalida{sufixo}", Ativo = true });

        // Regime padrão (sem configuração explícita) é Simples Nacional — CST não é válido.
        var perfil = new TributacaoCategoriaDto { CstIcms = "00" };

        await Assert.ThrowsAsync<DomainException>(
            () => categoriaService.SalvarTributacaoPadraoAsync(categoria.Id, perfil));
    }
}
