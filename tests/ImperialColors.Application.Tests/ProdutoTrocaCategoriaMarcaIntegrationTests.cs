using ImperialColors.Application.DTOs;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Entities;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Trocar a categoria (ou a marca, ou o fornecedor) de um produto existente e salvar.
///
/// O bug real: o operador mudava o produto de REVESTIMENTO para TINTA, a tela dizia que salvou
/// e fechava, mas o produto continuava em REVESTIMENTO. Nenhum erro, nada gravado.
///
/// Causa: <c>ObterPorIdAsync</c> carrega o produto com <c>Categoria</c>, <c>Marca</c> e
/// <c>Fornecedor</c> (as ANTIGAS) e o serviço só trocava os ids. Ao anexar o produto a um
/// contexto novo com <c>Update</c>, o EF segue a navegação — que ainda aponta para a categoria
/// antiga — e desfaz a troca do id. Só o banco de verdade mostra isso: um repositório mockado
/// aceitaria o id novo sem reclamar.
/// </summary>
[Collection(SemParalelismoCollection.Nome)]
public class ProdutoTrocaCategoriaMarcaIntegrationTests
{
    private static string Sufixo() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private sealed class Cenario : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required IDbContextFactory<AppDbContext> Fabrica { get; init; }
        public required Categoria CategoriaA { get; init; }
        public required Categoria CategoriaB { get; init; }
        public required Marca MarcaA { get; init; }
        public required Marca MarcaB { get; init; }
        public required Fornecedor FornecedorA { get; init; }
        public required Fornecedor FornecedorB { get; init; }
        public required Produto Produto { get; init; }

        public IProdutoService Servico => Provider.GetRequiredService<IProdutoService>();

        public async ValueTask DisposeAsync()
        {
            // Limpeza no banco real: o teste grava de verdade (o serviço abre os próprios
            // contextos), então apaga tudo o que criou, na ordem das chaves estrangeiras.
            await using var ctx = Fabrica.CreateDbContext();
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM movimentacoes_estoque WHERE produto_id = {Produto.Id}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM produtos WHERE id = {Produto.Id}");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM categorias WHERE id IN ({CategoriaA.Id}, {CategoriaB.Id})");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM marcas WHERE id IN ({MarcaA.Id}, {MarcaB.Id})");
            await ctx.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM fornecedores WHERE id IN ({FornecedorA.Id}, {FornecedorB.Id})");
            await Provider.DisposeAsync();
        }
    }

    private static async Task<Cenario?> CriarAsync()
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

        var s = Sufixo();
        var categoriaA = new Categoria { Nome = $"TC-A-{s}" };
        var categoriaB = new Categoria { Nome = $"TC-B-{s}" };
        var marcaA = new Marca { Nome = $"TM-A-{s}" };
        var marcaB = new Marca { Nome = $"TM-B-{s}" };
        var fornecedorA = new Fornecedor { Nome = $"TF-A-{s}" };
        var fornecedorB = new Fornecedor { Nome = $"TF-B-{s}" };
        ctx.AddRange(categoriaA, categoriaB, marcaA, marcaB, fornecedorA, fornecedorB);
        await ctx.SaveChangesAsync();

        var produto = new Produto
        {
            CodigoInterno = $"TC-{s}",
            Nome = $"Produto troca {s}",
            CategoriaId = categoriaA.Id,
            MarcaId = marcaA.Id,
            FornecedorId = fornecedorA.Id,
            QuantidadeEstoque = 5m,
            EstoqueMinimo = 1m,
            Unidade = "UN",
            PrecoVenda = 10m
        };
        ctx.Add(produto);
        await ctx.SaveChangesAsync();

        return new Cenario
        {
            Provider = provider, Fabrica = fabrica,
            CategoriaA = categoriaA, CategoriaB = categoriaB,
            MarcaA = marcaA, MarcaB = marcaB,
            FornecedorA = fornecedorA, FornecedorB = fornecedorB,
            Produto = produto
        };
    }

    /// <summary>O DTO que a tela de edição manda: tudo igual ao que está gravado, menos o que
    /// o operador trocou.</summary>
    private static AtualizarProdutoDto DtoDaEdicao(Cenario c, int categoriaId, int marcaId, int? fornecedorId) => new()
    {
        Id = c.Produto.Id,
        CodigoInterno = c.Produto.CodigoInterno,
        Nome = c.Produto.Nome,
        CategoriaId = categoriaId,
        MarcaId = marcaId,
        FornecedorId = fornecedorId,
        QuantidadeEstoque = c.Produto.QuantidadeEstoque,
        QuantidadeEstoqueOriginal = c.Produto.QuantidadeEstoque,
        EstoqueMinimo = c.Produto.EstoqueMinimo,
        Unidade = c.Produto.Unidade,
        PrecoVenda = c.Produto.PrecoVenda
    };

    private static async Task<Produto> LerAsync(Cenario c)
    {
        await using var ctx = c.Fabrica.CreateDbContext();
        return await ctx.Produtos.AsNoTracking().SingleAsync(p => p.Id == c.Produto.Id);
    }

    [Fact]
    public async Task TrocarACategoria_E_Salvar_GravaDeVerdade()
    {
        await using var cenario = await CriarAsync();
        if (cenario is null) return;

        await cenario.Servico.AtualizarAsync(
            cenario.Produto.Id,
            DtoDaEdicao(cenario, cenario.CategoriaB.Id, cenario.MarcaA.Id, cenario.FornecedorA.Id));

        var gravado = await LerAsync(cenario);
        Assert.Equal(cenario.CategoriaB.Id, gravado.CategoriaId);
    }

    [Fact]
    public async Task TrocarAMarca_E_Salvar_GravaDeVerdade()
    {
        await using var cenario = await CriarAsync();
        if (cenario is null) return;

        await cenario.Servico.AtualizarAsync(
            cenario.Produto.Id,
            DtoDaEdicao(cenario, cenario.CategoriaA.Id, cenario.MarcaB.Id, cenario.FornecedorA.Id));

        var gravado = await LerAsync(cenario);
        Assert.Equal(cenario.MarcaB.Id, gravado.MarcaId);
    }

    [Fact]
    public async Task TrocarOFornecedor_E_Salvar_GravaDeVerdade()
    {
        await using var cenario = await CriarAsync();
        if (cenario is null) return;

        await cenario.Servico.AtualizarAsync(
            cenario.Produto.Id,
            DtoDaEdicao(cenario, cenario.CategoriaA.Id, cenario.MarcaA.Id, cenario.FornecedorB.Id));

        var gravado = await LerAsync(cenario);
        Assert.Equal(cenario.FornecedorB.Id, gravado.FornecedorId);
    }

    /// <summary>Remover o fornecedor (campo opcional) também tem que valer — a mesma
    /// navegação antiga desfazia isso.</summary>
    [Fact]
    public async Task RemoverOFornecedor_E_Salvar_GravaDeVerdade()
    {
        await using var cenario = await CriarAsync();
        if (cenario is null) return;

        await cenario.Servico.AtualizarAsync(
            cenario.Produto.Id,
            DtoDaEdicao(cenario, cenario.CategoriaA.Id, cenario.MarcaA.Id, fornecedorId: null));

        var gravado = await LerAsync(cenario);
        Assert.Null(gravado.FornecedorId);
    }

    /// <summary>Os três de uma vez, como o operador faz numa edição só.</summary>
    [Fact]
    public async Task TrocarCategoriaMarcaEFornecedorJuntos_GravaOsTres()
    {
        await using var cenario = await CriarAsync();
        if (cenario is null) return;

        await cenario.Servico.AtualizarAsync(
            cenario.Produto.Id,
            DtoDaEdicao(cenario, cenario.CategoriaB.Id, cenario.MarcaB.Id, cenario.FornecedorB.Id));

        var gravado = await LerAsync(cenario);
        Assert.Equal((cenario.CategoriaB.Id, cenario.MarcaB.Id, cenario.FornecedorB.Id),
                     (gravado.CategoriaId, gravado.MarcaId, gravado.FornecedorId));
    }

    /// <summary>
    /// O que a tela recebe de volta depois de salvar tem que descrever o produto NOVO. Se a
    /// correção só anulasse as navegações, o retorno viria com categoria e marca em branco.
    /// </summary>
    [Fact]
    public async Task DepoisDeSalvar_OProdutoDevolvidoTrazOsNomesNovos()
    {
        await using var cenario = await CriarAsync();
        if (cenario is null) return;

        var devolvido = await cenario.Servico.AtualizarAsync(
            cenario.Produto.Id,
            DtoDaEdicao(cenario, cenario.CategoriaB.Id, cenario.MarcaB.Id, cenario.FornecedorB.Id));

        Assert.Equal(cenario.CategoriaB.Nome, devolvido.CategoriaNome);
        Assert.Equal(cenario.MarcaB.Nome, devolvido.MarcaNome);
    }

    /// <summary>Editar outro campo sem mexer em categoria/marca não pode perdê-las.</summary>
    [Fact]
    public async Task EditarSoOPreco_MantemCategoriaEMarca()
    {
        await using var cenario = await CriarAsync();
        if (cenario is null) return;

        var dto = DtoDaEdicao(cenario, cenario.CategoriaA.Id, cenario.MarcaA.Id, cenario.FornecedorA.Id);
        dto.PrecoVenda = 99.90m;
        await cenario.Servico.AtualizarAsync(cenario.Produto.Id, dto);

        var gravado = await LerAsync(cenario);
        Assert.Equal(99.90m, gravado.PrecoVenda);
        Assert.Equal((cenario.CategoriaA.Id, cenario.MarcaA.Id, cenario.FornecedorA.Id),
                     (gravado.CategoriaId, gravado.MarcaId, gravado.FornecedorId));
    }
}
