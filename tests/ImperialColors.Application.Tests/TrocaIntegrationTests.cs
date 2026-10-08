using ImperialColors.Application.DTOs;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Critérios obrigatórios:
/// 1) Produto GL 18L e BD persistem corretamente no banco.
/// 2) Troca de item R$100 por R$150 calcula diferença R$50 e atualiza estoques.
///
/// Usa a pilha de DI real (<c>AddInfrastructure</c>/<c>AddApplication</c>, serviços de
/// verdade) em vez de <c>FactoryDeContextoFixo</c> porque o que está sob teste é o fluxo
/// completo Venda → Troca com cálculo financeiro real — cada serviço abre seu próprio
/// <c>DbContext</c> pelo factory (padrão do projeto), o que não é compatível com envolver
/// tudo numa única transação externa revertida. Por isso cada teste limpa explicitamente o
/// que criou no <c>finally</c>, na ordem que respeita os FKs Restrict (Troca e ItemVenda
/// travam a exclusão de Produto; Troca também trava a de Venda).
///
/// Esta limpeza foi adicionada depois de eu encontrar, num banco de desenvolvimento real,
/// centenas de categorias/marcas/produtos com nomes como os gerados aqui
/// ("Cat-&lt;sufixo&gt;", "Tinta A &lt;sufixo&gt;") — evidência de que esta suíte vinha
/// rodando sem limpar há tempo.
/// </summary>
public class TrocaIntegrationTests
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

    /// <summary>Desfaz o que o teste criou, na ordem que os FKs Restrict exigem: Troca antes
    /// de Venda/Produto (trava os dois), Venda antes de Produto (a cascata de ItemVenda só
    /// libera o Produto depois que a Venda em si sai), MovimentacaoEstoque antes de Produto
    /// (também Restrict), Categoria/Marca por último.</summary>
    private static async Task LimparAsync(
        IDbContextFactory<AppDbContext> contextFactory,
        IReadOnlyList<int> trocaIds, IReadOnlyList<int> vendaIds, IReadOnlyList<int> produtoIds,
        IReadOnlyList<int> categoriaIds, IReadOnlyList<int> marcaIds)
    {
        await using var ctx = await contextFactory.CreateDbContextAsync();

        if (trocaIds.Count > 0)
            await ctx.Set<Domain.Entities.Troca>().IgnoreQueryFilters()
                .Where(t => trocaIds.Contains(t.Id)).ExecuteDeleteAsync();

        if (vendaIds.Count > 0)
            await ctx.Set<Domain.Entities.Venda>().IgnoreQueryFilters()
                .Where(v => vendaIds.Contains(v.Id)).ExecuteDeleteAsync();

        if (produtoIds.Count > 0)
        {
            await ctx.Set<Domain.Entities.MovimentacaoEstoque>().IgnoreQueryFilters()
                .Where(m => produtoIds.Contains(m.ProdutoId)).ExecuteDeleteAsync();
            await ctx.Set<Domain.Entities.Produto>().IgnoreQueryFilters()
                .Where(p => produtoIds.Contains(p.Id)).ExecuteDeleteAsync();
        }

        if (categoriaIds.Count > 0)
            await ctx.Set<Domain.Entities.Categoria>().IgnoreQueryFilters()
                .Where(c => categoriaIds.Contains(c.Id)).ExecuteDeleteAsync();

        if (marcaIds.Count > 0)
            await ctx.Set<Domain.Entities.Marca>().IgnoreQueryFilters()
                .Where(m => marcaIds.Contains(m.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task CadastrarProdutoGl18LeBaldeBD_DeveSalvarTamanhoEmbalagemEUnidadeCorretas()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var produtoService = scope.ServiceProvider.GetRequiredService<IProdutoService>();
        var categoriaRepo = scope.ServiceProvider.GetRequiredService<IRepository<Domain.Entities.Categoria>>();
        var marcaRepo = scope.ServiceProvider.GetRequiredService<IRepository<Domain.Entities.Marca>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var categoria = await categoriaRepo.AdicionarAsync(new Domain.Entities.Categoria { Nome = $"Cat-{sufixo}", Ativo = true });
        var marca = await marcaRepo.AdicionarAsync(new Domain.Entities.Marca { Nome = $"Marca-{sufixo}", Ativo = true });
        var produtoIds = new List<int>();

        try
        {
            // --- Teste 1: Galão 18L ---
            var galao18L = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
                Nome = $"Tinta Coral GL18L {sufixo}",
                CodigoInterno = $"GL18-{sufixo}",
                CodigoInternoDefinidoManualmente = true,
                CategoriaId = categoria.Id,
                MarcaId = marca.Id,
                Unidade = "GL",
                TamanhoEmbalagem = "18L",
                PrecoVenda = 150m,
                Custo = 90m,
                QuantidadeEstoque = 10,
                EstoqueMinimo = 1
            });
            produtoIds.Add(galao18L.Id);

            Assert.Equal("GL", galao18L.Unidade);
            Assert.Equal("18L", galao18L.TamanhoEmbalagem);
            Assert.Contains("18L", galao18L.NomeExibicao);

            // Verifica no banco diretamente
            await using var ctx = await contextFactory.CreateDbContextAsync();
            var prodBanco = await ctx.Set<Domain.Entities.Produto>()
                .IgnoreQueryFilters()
                .FirstAsync(p => p.Id == galao18L.Id);

            Assert.Equal("18L", prodBanco.TamanhoEmbalagem);

            // --- Teste 2: Balde BD ---
            var balde = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
                Nome = $"Tinta Balde BD {sufixo}",
                CodigoInterno = $"BD-{sufixo}",
                CodigoInternoDefinidoManualmente = true,
                CategoriaId = categoria.Id,
                MarcaId = marca.Id,
                Unidade = "BD",
                TamanhoEmbalagem = null,
                PrecoVenda = 80m,
                Custo = 50m,
                QuantidadeEstoque = 5,
                EstoqueMinimo = 1
            });
            produtoIds.Add(balde.Id);

            Assert.Equal("BD", balde.Unidade);
            // InputSanitizer.SanitizarTexto devolve "" para nulo/branco (mesmo contrato de
            // CodigoBarras/Observacoes neste serviço) — nunca null de fato.
            Assert.True(string.IsNullOrEmpty(balde.TamanhoEmbalagem));

            var baldeBanco = await ctx.Set<Domain.Entities.Produto>()
                .IgnoreQueryFilters()
                .FirstAsync(p => p.Id == balde.Id);
            Assert.Equal("BD", baldeBanco.Unidade);
        }
        finally
        {
            await LimparAsync(contextFactory, [], [], produtoIds, [categoria.Id], [marca.Id]);
        }
    }

    [Fact]
    public async Task Troca_ItemR100PorR150_DeveCalcularDiferenca50EAtualizarEstoques()
    {
        if (!TryConfigurar(out var provider)) return;
        await using var scope = provider.CreateAsyncScope();

        var produtoService = scope.ServiceProvider.GetRequiredService<IProdutoService>();
        var vendaService = scope.ServiceProvider.GetRequiredService<IVendaService>();
        var trocaService = scope.ServiceProvider.GetRequiredService<ITrocaService>();
        var categoriaRepo = scope.ServiceProvider.GetRequiredService<IRepository<Domain.Entities.Categoria>>();
        var marcaRepo = scope.ServiceProvider.GetRequiredService<IRepository<Domain.Entities.Marca>>();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var categoria = await categoriaRepo.AdicionarAsync(new Domain.Entities.Categoria { Nome = $"Cat-{sufixo}", Ativo = true });
        var marca = await marcaRepo.AdicionarAsync(new Domain.Entities.Marca { Nome = $"Marca-{sufixo}", Ativo = true });
        var produtoIds = new List<int>();
        var vendaIds = new List<int>();
        var trocaIds = new List<int>();

        try
        {
            // Produto devolvido: R$100 com 5 em estoque
            var prodDevolvido = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
                Nome = $"Tinta A {sufixo}",
                CodigoInterno = $"PA-{sufixo}",
                CodigoInternoDefinidoManualmente = true,
                CategoriaId = categoria.Id,
                MarcaId = marca.Id,
                Unidade = "GL",
                TamanhoEmbalagem = "18L",
                PrecoVenda = 100m,
                Custo = 60m,
                QuantidadeEstoque = 5,
                EstoqueMinimo = 1
            });
            produtoIds.Add(prodDevolvido.Id);

            // Produto novo: R$150 com 10 em estoque
            var prodNovo = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
                Nome = $"Tinta B {sufixo}",
                CodigoInterno = $"PB-{sufixo}",
                CodigoInternoDefinidoManualmente = true,
                CategoriaId = categoria.Id,
                MarcaId = marca.Id,
                Unidade = "GL",
                TamanhoEmbalagem = "18L",
                PrecoVenda = 150m,
                Custo = 90m,
                QuantidadeEstoque = 10,
                EstoqueMinimo = 1
            });
            produtoIds.Add(prodNovo.Id);

            // CriarAsync já grava a venda finalizada (transação única, sem etapa "aberta"
            // separada — ver EstoqueAtomicoHelper/CriarOnlineInternoAsync).
            var venda = await vendaService.CriarAsync(new CriarVendaDto
            {
                FormaPagamento = FormaPagamento.Dinheiro,
                ValorPago = 100m,
                Troco = 0m,
                Itens = new List<CriarItemVendaDto>
                {
                    new() { ProdutoId = prodDevolvido.Id, Quantidade = 1, PrecoUnitario = 100m, Desconto = 0 }
                }
            });
            vendaIds.Add(venda.Id);
            Assert.Equal(StatusVenda.Finalizada, venda.Status);

            // Carrega venda com itens para obter o ItemVendaId
            var vendaComItens = await vendaService.ObterComItensAsync(venda.Id);
            Assert.NotNull(vendaComItens);
            var itemOrigem = vendaComItens!.Itens.First();

            // Registra troca: devolve produto A (R$100), leva produto B (R$150)
            var trocaDto = await trocaService.RegistrarAsync(new RegistrarTrocaDto
            {
                VendaOrigemId = venda.Id,
                ItemVendaOrigemId = itemOrigem.Id,
                QuantidadeDevolvida = 1,
                RetornarAoEstoque = true,
                ProdutoNovoId = prodNovo.Id,
                QuantidadeNova = 1,
                PrecoUnitarioNovo = 150m,
                FormaPagamentoDiferenca = FormaPagamento.Pix,
                Usuario = "TestRunner"
            });
            trocaIds.Add(trocaDto.Id);

            // --- Assertions financeiras ---
            Assert.Equal(100m, trocaDto.ValorTotalDevolvido);
            Assert.Equal(150m, trocaDto.ValorTotalNovo);
            Assert.Equal(50m, trocaDto.DiferencaValor);  // diferença a receber = R$50
            Assert.Equal(FormaPagamento.Pix, trocaDto.FormaPagamentoDiferenca);

            // --- Assertions de estoque ---
            await using var ctx = await contextFactory.CreateDbContextAsync();

            var estoqueDevolvidoAtual = (await ctx.Set<Domain.Entities.Produto>()
                .IgnoreQueryFilters()
                .FirstAsync(p => p.Id == prodDevolvido.Id)).QuantidadeEstoque;

            var estoqueNovoAtual = (await ctx.Set<Domain.Entities.Produto>()
                .IgnoreQueryFilters()
                .FirstAsync(p => p.Id == prodNovo.Id)).QuantidadeEstoque;

            // RetornarAoEstoque=true: estoque A volta para 5 (5 - 1 saída venda + 1 retorno troca = 5)
            Assert.Equal(5m, estoqueDevolvidoAtual);

            // Produto B saiu 1 da troca: 10 - 1 = 9
            Assert.Equal(9m, estoqueNovoAtual);
        }
        finally
        {
            await LimparAsync(contextFactory, trocaIds, vendaIds, produtoIds, [categoria.Id], [marca.Id]);
        }
    }
}
