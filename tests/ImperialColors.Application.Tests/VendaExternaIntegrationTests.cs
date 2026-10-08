using ImperialColors.Application.DTOs;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Entities;
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
/// Critérios:
/// 1) Venda mista (estoque + manual) grava faturamento e baixa só item vinculado.
/// 2) Estoque insuficiente provoca rollback completo.
/// </summary>
public class VendaExternaIntegrationTests
{
    private static bool TryConfigurar(out ServiceProvider provider)
    {
        provider = null!;
        if (!IntegrationTestGuard.TryObterConnectionString(out var cs))
            return false;

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(cs);
        services.AddApplication();
        provider = services.BuildServiceProvider();
        return true;
    }

    /// <summary>Categoria/Marca são obrigatórias em <see cref="ProdutoValidator"/> — cria um
    /// par isolado (sufixo único) para não colidir com outros testes rodando em paralelo.</summary>
    private static async Task<(int CategoriaId, int MarcaId)> CriarCategoriaEMarcaAsync(
        IServiceProvider serviceProvider, string sufixo)
    {
        var categoriaRepo = serviceProvider.GetRequiredService<IRepository<Categoria>>();
        var marcaRepo = serviceProvider.GetRequiredService<IRepository<Marca>>();

        var categoria = await categoriaRepo.AdicionarAsync(new Categoria { Nome = $"CatVendaExterna{sufixo}", Ativo = true });
        var marca = await marcaRepo.AdicionarAsync(new Marca { Nome = $"MarcaVendaExterna{sufixo}", Ativo = true });
        return (categoria.Id, marca.Id);
    }

    [Fact]
    public async Task RegistrarVendaExternaMista_DeveBaixarEstoqueApenasDoProdutoVinculado()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var produtoService = scope.ServiceProvider.GetRequiredService<IProdutoService>();
        var vendaExternaService = scope.ServiceProvider.GetRequiredService<IVendaExternaService>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var (categoriaId, marcaId) = await CriarCategoriaEMarcaAsync(scope.ServiceProvider, sufixo);
        var produto = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            Nome = $"Tinta Externa {sufixo}",
            CodigoInterno = $"EXT-{sufixo}",
            CodigoInternoDefinidoManualmente = true,
            CodigoBarras = $"789{sufixo}",
            CategoriaId = categoriaId,
            MarcaId = marcaId,
            PrecoVenda = 100m,
            QuantidadeEstoque = 10,
            EstoqueMinimo = 1
        });

        const decimal qtdBaixa = 2m;
        var venda = await vendaExternaService.RegistrarAsync(new RegistrarVendaExternaDto
        {
            Usuario = "Teste",
            Itens =
            [
                new RegistrarItemVendaExternaDto
                {
                    ProdutoId = produto.Id,
                    NomeProduto = produto.Nome,
                    CodigoBarras = produto.CodigoBarras,
                    Quantidade = qtdBaixa,
                    PrecoBase = produto.PrecoVenda,
                    PrecoUnitario = 95m
                },
                new RegistrarItemVendaExternaDto
                {
                    ProdutoId = null,
                    NomeProduto = "Cor personalizada avulsa",
                    Quantidade = 1,
                    PrecoBase = 0,
                    PrecoUnitario = 50m
                }
            ]
        });

        Assert.StartsWith("EXT-", venda.NumeroVendaExterna);
        Assert.Equal(2, venda.Itens.Count);
        Assert.Equal(240m, venda.Total);

        await using var ctx = await contextFactory.CreateDbContextAsync();
        var prodBanco = await ctx.Set<Domain.Entities.Produto>().FirstAsync(p => p.Id == produto.Id);
        Assert.Equal(10m - qtdBaixa, prodBanco.QuantidadeEstoque);

        var movimentacoes = await ctx.Set<Domain.Entities.MovimentacaoEstoque>()
            .Where(m => m.VendaExternaId == venda.Id)
            .ToListAsync();
        Assert.Single(movimentacoes);
        Assert.Equal(qtdBaixa, movimentacoes[0].Quantidade);
    }

    [Fact]
    public async Task RegistrarVendaExterna_EstoqueInsuficiente_DeveFazerRollback()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var produtoService = scope.ServiceProvider.GetRequiredService<IProdutoService>();
        var vendaExternaService = scope.ServiceProvider.GetRequiredService<IVendaExternaService>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var (categoriaId, marcaId) = await CriarCategoriaEMarcaAsync(scope.ServiceProvider, sufixo);
        var produto = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            Nome = $"Produto Rollback {sufixo}",
            CodigoInterno = $"RB-{sufixo}",
            CodigoInternoDefinidoManualmente = true,
            CategoriaId = categoriaId,
            MarcaId = marcaId,
            PrecoVenda = 50m,
            QuantidadeEstoque = 1,
            EstoqueMinimo = 0
        });

        var antes = await vendaExternaService.ObterTodosAsync();

        await Assert.ThrowsAsync<DomainException>(() => vendaExternaService.RegistrarAsync(new RegistrarVendaExternaDto
        {
            Itens =
            [
                new RegistrarItemVendaExternaDto
                {
                    ProdutoId = produto.Id,
                    NomeProduto = produto.Nome,
                    Quantidade = 5,
                    PrecoBase = produto.PrecoVenda,
                    PrecoUnitario = produto.PrecoVenda
                }
            ]
        }));

        var depois = await vendaExternaService.ObterTodosAsync();
        Assert.Equal(antes.Count(), depois.Count());

        await using var ctx = await contextFactory.CreateDbContextAsync();
        var prodBanco = await ctx.Set<Domain.Entities.Produto>().FirstAsync(p => p.Id == produto.Id);
        Assert.Equal(1m, prodBanco.QuantidadeEstoque);
    }

    [Fact]
    public async Task EditarVendaExterna_DiminuirQuantidade_DeveReporDiferencaNoEstoque()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var produtoService = scope.ServiceProvider.GetRequiredService<IProdutoService>();
        var vendaExternaService = scope.ServiceProvider.GetRequiredService<IVendaExternaService>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var (categoriaId, marcaId) = await CriarCategoriaEMarcaAsync(scope.ServiceProvider, sufixo);
        var produto = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            Nome = $"Galão Externo {sufixo}",
            CodigoInterno = $"GAL-{sufixo}",
            CodigoInternoDefinidoManualmente = true,
            CategoriaId = categoriaId,
            MarcaId = marcaId,
            PrecoVenda = 80m,
            QuantidadeEstoque = 20,
            EstoqueMinimo = 1,
            Unidade = "GL"
        });

        const decimal qtdInicial = 5m;
        const decimal qtdFinal = 3m;

        var venda = await vendaExternaService.RegistrarAsync(new RegistrarVendaExternaDto
        {
            Usuario = "Teste",
            Itens =
            [
                new RegistrarItemVendaExternaDto
                {
                    ProdutoId = produto.Id,
                    NomeProduto = produto.Nome,
                    Quantidade = qtdInicial,
                    PrecoBase = produto.PrecoVenda,
                    PrecoUnitario = produto.PrecoVenda
                }
            ]
        });

        var itemId = venda.Itens.Single().Id;

        await vendaExternaService.AtualizarAsync(new AtualizarVendaExternaDto
        {
            Id = venda.Id,
            Usuario = "Teste",
            Itens =
            [
                new AtualizarItemVendaExternaDto
                {
                    Id = itemId,
                    ProdutoId = produto.Id,
                    NomeProduto = produto.Nome,
                    Quantidade = qtdFinal,
                    PrecoBase = produto.PrecoVenda,
                    PrecoUnitario = produto.PrecoVenda
                }
            ]
        });

        await using var ctx = await contextFactory.CreateDbContextAsync();
        var prodBanco = await ctx.Set<Domain.Entities.Produto>().FirstAsync(p => p.Id == produto.Id);
        Assert.Equal(20m - qtdFinal, prodBanco.QuantidadeEstoque);
    }

    [Fact]
    public async Task ExcluirVendaExterna_DeveRemoverDoBancoEReporEstoque()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var produtoService = scope.ServiceProvider.GetRequiredService<IProdutoService>();
        var vendaExternaService = scope.ServiceProvider.GetRequiredService<IVendaExternaService>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var (categoriaId, marcaId) = await CriarCategoriaEMarcaAsync(scope.ServiceProvider, sufixo);
        var produto = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            Nome = $"Prod Exclusão {sufixo}",
            CodigoInterno = $"DEL-{sufixo}",
            CodigoInternoDefinidoManualmente = true,
            CategoriaId = categoriaId,
            MarcaId = marcaId,
            PrecoVenda = 60m,
            QuantidadeEstoque = 15,
            EstoqueMinimo = 0
        });

        const decimal qtdVendida = 4m;
        var venda = await vendaExternaService.RegistrarAsync(new RegistrarVendaExternaDto
        {
            Itens =
            [
                new RegistrarItemVendaExternaDto
                {
                    ProdutoId = produto.Id,
                    NomeProduto = produto.Nome,
                    Quantidade = qtdVendida,
                    PrecoBase = produto.PrecoVenda,
                    PrecoUnitario = produto.PrecoVenda
                }
            ]
        });

        await vendaExternaService.ExcluirFisicamenteAsync(venda.Id);

        await using var ctx = await contextFactory.CreateDbContextAsync();
        var vendaNoBanco = await ctx.Set<Domain.Entities.VendaExterna>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Id == venda.Id);
        Assert.Null(vendaNoBanco);

        var prodBanco = await ctx.Set<Domain.Entities.Produto>().FirstAsync(p => p.Id == produto.Id);
        Assert.Equal(15m, prodBanco.QuantidadeEstoque);
    }
}
