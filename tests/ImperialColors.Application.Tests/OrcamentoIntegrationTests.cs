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
/// Critérios:
/// 1) Orçamento misto (estoque + manual) grava totais e NÃO encosta no estoque.
/// 2) Editar troca a lista de itens sem duplicar e sem movimentar estoque.
/// 3) Validade no passado é recusada.
/// </summary>
// AlterarStatus_DeveMarcarAprovadoSemGerarVenda compara a contagem total de vendas do banco.
[Collection(SemParalelismoCollection.Nome)]
public class OrcamentoIntegrationTests
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

    private static async Task<(int CategoriaId, int MarcaId)> CriarCategoriaEMarcaAsync(
        IServiceProvider serviceProvider, string sufixo)
    {
        var categoriaRepo = serviceProvider.GetRequiredService<IRepository<Categoria>>();
        var marcaRepo = serviceProvider.GetRequiredService<IRepository<Marca>>();

        var categoria = await categoriaRepo.AdicionarAsync(new Categoria { Nome = $"CatOrcamento{sufixo}", Ativo = true });
        var marca = await marcaRepo.AdicionarAsync(new Marca { Nome = $"MarcaOrcamento{sufixo}", Ativo = true });
        return (categoria.Id, marca.Id);
    }

    private static async Task<ProdutoDto> CriarProdutoAsync(
        IProdutoService produtoService, int categoriaId, int marcaId, string sufixo, decimal estoque)
        => await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            Nome = $"Tinta Orçamento {sufixo}",
            CodigoInterno = $"ORC-{sufixo}",
            CodigoInternoDefinidoManualmente = true,
            CategoriaId = categoriaId,
            MarcaId = marcaId,
            PrecoVenda = 100m,
            QuantidadeEstoque = estoque,
            EstoqueMinimo = 1
        });

    /// <summary>
    /// AUDITORIA (15/09): esta suíte criava categoria/marca/produto/orçamento direto no banco
    /// compartilhado sem nenhuma limpeza — confirmado ao vivo que já tinha deixado 17
    /// orçamentos e 5 categorias de lixo acumulados. Mesmo padrão já corrigido em
    /// TrocaIntegrationTests.LimparAsync; replicado aqui.
    /// </summary>
    private static async Task LimparAsync(
        IDbContextFactory<AppDbContext> contextFactory,
        IReadOnlyList<int> orcamentoIds,
        IReadOnlyList<int> produtoIds,
        IReadOnlyList<int> categoriaIds,
        IReadOnlyList<int> marcaIds)
    {
        await using var ctx = await contextFactory.CreateDbContextAsync();

        if (orcamentoIds.Count > 0)
        {
            await ctx.Set<ItemOrcamento>().IgnoreQueryFilters()
                .Where(i => orcamentoIds.Contains(i.OrcamentoId)).ExecuteDeleteAsync();
            await ctx.Set<Orcamento>().IgnoreQueryFilters()
                .Where(o => orcamentoIds.Contains(o.Id)).ExecuteDeleteAsync();
        }

        if (produtoIds.Count > 0)
        {
            await ctx.Set<MovimentacaoEstoque>().IgnoreQueryFilters()
                .Where(m => produtoIds.Contains(m.ProdutoId)).ExecuteDeleteAsync();
            await ctx.Set<Produto>().IgnoreQueryFilters()
                .Where(p => produtoIds.Contains(p.Id)).ExecuteDeleteAsync();
        }

        if (categoriaIds.Count > 0)
            await ctx.Set<Categoria>().IgnoreQueryFilters()
                .Where(c => categoriaIds.Contains(c.Id)).ExecuteDeleteAsync();

        if (marcaIds.Count > 0)
            await ctx.Set<Marca>().IgnoreQueryFilters()
                .Where(m => marcaIds.Contains(m.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task RegistrarOrcamentoMisto_DeveCalcularTotaisSemTocarNoEstoque()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var produtoService = scope.ServiceProvider.GetRequiredService<IProdutoService>();
        var orcamentoService = scope.ServiceProvider.GetRequiredService<IOrcamentoService>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var (categoriaId, marcaId) = await CriarCategoriaEMarcaAsync(scope.ServiceProvider, sufixo);
        var produto = await CriarProdutoAsync(produtoService, categoriaId, marcaId, sufixo, estoque: 10m);

        try
        {
            // O cadastro do produto já gera a movimentação de entrada inicial: o que interessa
            // aqui é que o orçamento não acrescente nenhuma outra.
            await using var ctxAntes = await contextFactory.CreateDbContextAsync();
            var movimentacoesAntes = await ctxAntes.Set<MovimentacaoEstoque>()
                .CountAsync(m => m.ProdutoId == produto.Id);

            var orcamento = await orcamentoService.RegistrarAsync(new RegistrarOrcamentoDto
            {
                NomeCliente = $"Cliente Orçamento {sufixo}",
                TelefoneCliente = "11999998888",
                DataValidade = DateTime.Today.AddDays(7),
                Desconto = 40m,
                Usuario = "Teste",
                Itens =
                [
                    new ItemOrcamentoEntradaDto
                    {
                        ProdutoId = produto.Id,
                        NomeProduto = produto.Nome,
                        CodigoProduto = produto.CodigoInterno,
                        Unidade = produto.Unidade,
                        Quantidade = 2m,
                        PrecoUnitario = 100m
                    },
                    new ItemOrcamentoEntradaDto
                    {
                        ProdutoId = null,
                        NomeProduto = "Mão de obra de pintura",
                        Unidade = "UN",
                        Quantidade = 1m,
                        PrecoUnitario = 300m
                    }
                ]
            });

            try
            {
                Assert.StartsWith("ORC-", orcamento.NumeroOrcamento);
                Assert.Equal(2, orcamento.Itens.Count);
                Assert.Equal(500m, orcamento.Subtotal);
                Assert.Equal(460m, orcamento.Total);
                Assert.Equal(StatusOrcamento.Aberto, orcamento.Status);
                Assert.False(orcamento.Expirado);

                // A promessa do módulo: orçamento não reserva nem baixa estoque.
                await using var ctx = await contextFactory.CreateDbContextAsync();
                var prodBanco = await ctx.Set<Produto>().FirstAsync(p => p.Id == produto.Id);
                Assert.Equal(10m, prodBanco.QuantidadeEstoque);

                var movimentacoesDepois = await ctx.Set<MovimentacaoEstoque>()
                    .CountAsync(m => m.ProdutoId == produto.Id);
                Assert.Equal(movimentacoesAntes, movimentacoesDepois);
            }
            finally
            {
                await LimparAsync(contextFactory, [orcamento.Id], [], [], []);
            }
        }
        finally
        {
            await LimparAsync(contextFactory, [], [produto.Id], [categoriaId], [marcaId]);
        }
    }

    [Fact]
    public async Task EditarOrcamento_DeveSubstituirItensSemDuplicar()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var orcamentoService = scope.ServiceProvider.GetRequiredService<IOrcamentoService>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];

        var orcamento = await orcamentoService.RegistrarAsync(new RegistrarOrcamentoDto
        {
            NomeCliente = $"Cliente Edição {sufixo}",
            DataValidade = DateTime.Today.AddDays(3),
            Usuario = "Teste",
            Itens =
            [
                new ItemOrcamentoEntradaDto { NomeProduto = "Item A", Quantidade = 1m, PrecoUnitario = 100m },
                new ItemOrcamentoEntradaDto { NomeProduto = "Item B", Quantidade = 2m, PrecoUnitario = 50m }
            ]
        });

        try
        {
            Assert.Equal(200m, orcamento.Total);

            var editado = await orcamentoService.AtualizarAsync(new AtualizarOrcamentoDto
            {
                Id = orcamento.Id,
                NomeCliente = $"Cliente Edição {sufixo} (revisado)",
                DataValidade = DateTime.Today.AddDays(10),
                Desconto = 25m,
                Usuario = "Teste",
                Itens = [new ItemOrcamentoEntradaDto { NomeProduto = "Item C", Quantidade = 3m, PrecoUnitario = 75m }]
            });

            Assert.Equal(orcamento.NumeroOrcamento, editado.NumeroOrcamento);
            Assert.Single(editado.Itens);
            Assert.Equal("Item C", editado.Itens[0].NomeProduto);
            Assert.Equal(225m, editado.Subtotal);
            Assert.Equal(200m, editado.Total);

            // Itens antigos não podem sobrar órfãos apontando para o mesmo orçamento.
            await using var ctx = await contextFactory.CreateDbContextAsync();
            var itensNoBanco = await ctx.Set<ItemOrcamento>()
                .IgnoreQueryFilters()
                .Where(i => i.OrcamentoId == orcamento.Id)
                .CountAsync();
            Assert.Equal(1, itensNoBanco);
        }
        finally
        {
            await LimparAsync(contextFactory, [orcamento.Id], [], [], []);
        }
    }

    [Fact]
    public async Task AlterarStatus_DeveMarcarAprovadoSemGerarVenda()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var orcamentoService = scope.ServiceProvider.GetRequiredService<IOrcamentoService>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];

        await using var ctx = await contextFactory.CreateDbContextAsync();
        var vendasAntes = await ctx.Set<Venda>().CountAsync();

        var orcamento = await orcamentoService.RegistrarAsync(new RegistrarOrcamentoDto
        {
            NomeCliente = $"Cliente Aprovação {sufixo}",
            DataValidade = DateTime.Today.AddDays(5),
            Usuario = "Teste",
            Itens = [new ItemOrcamentoEntradaDto { NomeProduto = "Serviço", Quantidade = 1m, PrecoUnitario = 120m }]
        });

        try
        {
            var aprovado = await orcamentoService.AlterarStatusAsync(orcamento.Id, StatusOrcamento.Aprovado);

            Assert.Equal(StatusOrcamento.Aprovado, aprovado.Status);
            Assert.Equal("Aprovado", aprovado.StatusDescricao);

            await using var ctxDepois = await contextFactory.CreateDbContextAsync();
            Assert.Equal(vendasAntes, await ctxDepois.Set<Venda>().CountAsync());
        }
        finally
        {
            await LimparAsync(contextFactory, [orcamento.Id], [], [], []);
        }
    }

    [Fact]
    public async Task RegistrarOrcamento_ComValidadeNoPassado_DeveRecusar()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var orcamentoService = scope.ServiceProvider.GetRequiredService<IOrcamentoService>();

        await Assert.ThrowsAsync<DomainException>(() => orcamentoService.RegistrarAsync(new RegistrarOrcamentoDto
        {
            NomeCliente = "Cliente Validade Vencida",
            DataValidade = DateTime.Today.AddDays(-1),
            Itens = [new ItemOrcamentoEntradaDto { NomeProduto = "Item", Quantidade = 1m, PrecoUnitario = 10m }]
        }));
    }

    [Fact]
    public async Task RegistrarOrcamento_ComDescontoMaiorQueSubtotal_DeveRecusar()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var orcamentoService = scope.ServiceProvider.GetRequiredService<IOrcamentoService>();

        await Assert.ThrowsAsync<DomainException>(() => orcamentoService.RegistrarAsync(new RegistrarOrcamentoDto
        {
            NomeCliente = "Cliente Desconto Alto",
            DataValidade = DateTime.Today.AddDays(2),
            Desconto = 500m,
            Itens = [new ItemOrcamentoEntradaDto { NomeProduto = "Item", Quantidade = 1m, PrecoUnitario = 100m }]
        }));
    }
}
