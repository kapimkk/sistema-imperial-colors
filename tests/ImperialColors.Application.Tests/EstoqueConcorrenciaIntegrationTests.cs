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
/// Prova de que a correção de concorrência (EstoqueAtomicoHelper + transação única em
/// VendaRepository.CriarComBaixaEstoqueTransacionalAsync) elimina o "lost update" clássico:
/// dois PDVs vendendo a última unidade do mesmo produto ao mesmo tempo. Antes da correção,
/// as duas vendas liam QuantidadeEstoque=1 antes uma da outra escrever e ambas conseguiam
/// finalizar, deixando o estoque em -1. Requer RUN_INTEGRATION_TESTS=true e PostgreSQL
/// acessível via .env (mesmo requisito dos demais testes de integração deste projeto).
/// </summary>
public class EstoqueConcorrenciaIntegrationTests
{
    private static bool TryCarregarConfig(out ServiceProvider provider, out IDbContextFactory<AppDbContext> contextFactory)
    {
        provider = null!;
        contextFactory = null!;

        if (!IntegrationTestGuard.TryObterConnectionString(out var cs))
            return false;

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(cs);
        services.AddApplication();

        provider = services.BuildServiceProvider();
        contextFactory = provider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var dbContext = contextFactory.CreateDbContext();
        dbContext.Database.Migrate();
        return true;
    }

    [Fact]
    public async Task CriarAsync_DuasVendasConcorrentesNaUltimaUnidade_ApenasUmaVendeEEstoqueNuncaFicaNegativo()
    {
        if (!TryCarregarConfig(out var provider, out var contextFactory))
            return;

        await using var scopeSetup = provider.CreateAsyncScope();
        var produtoService = scopeSetup.ServiceProvider.GetRequiredService<IProdutoService>();
        var categoriaRepo = scopeSetup.ServiceProvider.GetRequiredService<IRepository<Categoria>>();
        var marcaRepo = scopeSetup.ServiceProvider.GetRequiredService<IRepository<Marca>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var categoria = await categoriaRepo.AdicionarAsync(new Categoria { Nome = $"CatConcorrencia{sufixo}", Ativo = true });
        var marca = await marcaRepo.AdicionarAsync(new Marca { Nome = $"MarcaConcorrencia{sufixo}", Ativo = true });

        var codigo = await produtoService.GerarProximoCodigoInternoAsync();
        var produto = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            CodigoInterno = codigo,
            Nome = $"ProdutoConcorrencia{sufixo}",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            Custo = 5m,
            PrecoVenda = 10m,
            QuantidadeEstoque = 1, // só resta 1 unidade — o cenário exato de disputa entre 2 PDVs
            EstoqueMinimo = 0,
            Unidade = "UN"
        });

        VendaDto? vendaVencedora = null;
        try
        {
            // Cada "PDV" usa seu próprio DI scope (=> seu próprio DbContext por chamada,
            // via IDbContextFactory), replicando duas máquinas distintas na rede local
            // finalizando a venda da mesma última unidade no mesmo instante.
            async Task<VendaDto> VenderUmaUnidadeAsync()
            {
                await using var scopeVenda = provider.CreateAsyncScope();
                var vendaService = scopeVenda.ServiceProvider.GetRequiredService<IVendaService>();
                return await vendaService.CriarAsync(new CriarVendaDto
                {
                    Usuario = "teste_concorrencia",
                    FormaPagamento = FormaPagamento.Dinheiro,
                    ValorPago = 10m,
                    Itens =
                    [
                        new CriarItemVendaDto { ProdutoId = produto.Id, Quantidade = 1, PrecoUnitario = 10m }
                    ]
                });
            }

            var tarefa1 = VenderUmaUnidadeAsync();
            var tarefa2 = VenderUmaUnidadeAsync();
            var resultados = new[] { tarefa1, tarefa2 };

            // Uma das duas tasks deve falhar (estoque insuficiente) — isso é esperado,
            // então apenas aguardamos as duas terminarem sem deixar a exceção propagar
            // daqui; o estado final de cada task é inspecionado abaixo.
            try { await Task.WhenAll(resultados); } catch (DomainException) { }

            var sucesso = resultados.Where(t => t.IsCompletedSuccessfully).ToList();
            var falhas = resultados.Where(t => t.IsFaulted).ToList();

            // O ponto central da correção: nunca as duas vendas passam ao mesmo tempo.
            // (Assert.Equal(1, ...Count) em vez de Assert.Single: como os elementos são
            // Task<VendaDto>, Assert.Single devolveria a task e o compilador confundiria
            // isso com uma chamada assíncrona esquecida — CS4014.)
            Assert.Equal(1, sucesso.Count);
            Assert.Equal(1, falhas.Count);

            // .Result é seguro aqui: a task já está em IsCompletedSuccessfully (filtrada
            // acima), então não há bloqueio real — é apenas leitura do valor já pronto.
#pragma warning disable xUnit1031
            vendaVencedora = sucesso[0].Result;
#pragma warning restore xUnit1031

            var erro = Assert.IsType<DomainException>(falhas[0].Exception!.InnerException);
            Assert.Contains("Estoque insuficiente", erro.Message);

            var produtoFinal = await produtoService.ObterPorIdAsync(produto.Id);
            Assert.NotNull(produtoFinal);
            // Nunca negativo (o bug original permitia -1 aqui) e exatamente zero
            // (a única unidade foi vendida uma única vez, não duas).
            Assert.Equal(0m, produtoFinal!.QuantidadeEstoque);
        }
        finally
        {
            // Mesma convenção dos demais testes de integração de venda: cancela (não
            // hard-delete) para repor estoque — o produto/categoria/marca de teste
            // permanecem no banco (nome com sufixo único, identificável como teste),
            // já que produtos com histórico de venda não podem ser excluídos fisicamente.
            if (vendaVencedora is not null)
            {
                await using var scopeCancelamento = provider.CreateAsyncScope();
                var vendaService = scopeCancelamento.ServiceProvider.GetRequiredService<IVendaService>();
                try { await vendaService.CancelarAsync(vendaVencedora.Id); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Prova de que editar um produto sem tocar no campo de quantidade nunca apaga uma
    /// venda concorrente: admin abre a tela de edição (vê QuantidadeEstoque=10), enquanto
    /// isso uma venda de 3 unidades é finalizada no PDV (estoque real cai para 7), e só
    /// depois o admin salva a edição com o mesmo valor que via (10) — sem ter mexido nele.
    /// Antes da correção, o AtualizarAsync fazia um UPDATE de linha inteira que sobrescrevia
    /// QuantidadeEstoque de volta para 10, apagando silenciosamente a venda.
    /// </summary>
    [Fact]
    public async Task AtualizarAsync_SemAlterarQuantidade_NaoApagaVendaConcorrente()
    {
        if (!TryCarregarConfig(out var provider, out _))
            return;

        await using var scopeSetup = provider.CreateAsyncScope();
        var produtoService = scopeSetup.ServiceProvider.GetRequiredService<IProdutoService>();
        var categoriaRepo = scopeSetup.ServiceProvider.GetRequiredService<IRepository<Categoria>>();
        var marcaRepo = scopeSetup.ServiceProvider.GetRequiredService<IRepository<Marca>>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var categoria = await categoriaRepo.AdicionarAsync(new Categoria { Nome = $"CatEdicao{sufixo}", Ativo = true });
        var marca = await marcaRepo.AdicionarAsync(new Marca { Nome = $"MarcaEdicao{sufixo}", Ativo = true });

        var produto = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            CodigoInterno = await produtoService.GerarProximoCodigoInternoAsync(),
            Nome = $"ProdutoEdicao{sufixo}",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            Custo = 5m,
            PrecoVenda = 10m,
            QuantidadeEstoque = 10,
            EstoqueMinimo = 0,
            Unidade = "UN"
        });

        // "Admin abre a tela de edição": carrega o produto e guarda o que viu (baseline=10).
        var carregadoParaEdicao = await produtoService.ObterPorIdAsync(produto.Id);
        Assert.NotNull(carregadoParaEdicao);

        VendaDto? venda = null;
        try
        {
            // Enquanto o formulário de edição está "aberto", uma venda de 3 unidades acontece.
            await using var scopeVenda = provider.CreateAsyncScope();
            var vendaService = scopeVenda.ServiceProvider.GetRequiredService<IVendaService>();
            venda = await vendaService.CriarAsync(new CriarVendaDto
            {
                Usuario = "teste_edicao_concorrente",
                FormaPagamento = FormaPagamento.Dinheiro,
                ValorPago = 30m,
                Itens = [new CriarItemVendaDto { ProdutoId = produto.Id, Quantidade = 3, PrecoUnitario = 10m }]
            });

            // Admin salva a edição — mudou só o nome, não tocou no campo de quantidade,
            // que continua exibindo o valor que ele carregou (10).
            var dtoEdicao = new AtualizarProdutoDto
            {
                Id = produto.Id,
                CodigoInterno = carregadoParaEdicao!.CodigoInterno,
                Nome = $"{carregadoParaEdicao.Nome} (editado)",
                CategoriaId = carregadoParaEdicao.CategoriaId,
                MarcaId = carregadoParaEdicao.MarcaId,
                Custo = carregadoParaEdicao.Custo,
                PrecoVenda = carregadoParaEdicao.PrecoVenda,
                QuantidadeEstoque = carregadoParaEdicao.QuantidadeEstoque, // 10 — igual ao que ele viu
                QuantidadeEstoqueOriginal = carregadoParaEdicao.QuantidadeEstoque, // baseline capturado no load da tela
                EstoqueMinimo = carregadoParaEdicao.EstoqueMinimo,
                Unidade = carregadoParaEdicao.Unidade
            };
            var atualizado = await produtoService.AtualizarAsync(produto.Id, dtoEdicao);

            // A venda de 3 unidades deve continuar valendo: 10 - 3 = 7, não 10.
            Assert.Equal(7m, atualizado.QuantidadeEstoque);

            var produtoFinal = await produtoService.ObterPorIdAsync(produto.Id);
            Assert.Equal(7m, produtoFinal!.QuantidadeEstoque);
            Assert.Equal($"{carregadoParaEdicao.Nome} (editado)", produtoFinal.Nome);
        }
        finally
        {
            if (venda is not null)
            {
                await using var scopeCancelamento = provider.CreateAsyncScope();
                var vendaService = scopeCancelamento.ServiceProvider.GetRequiredService<IVendaService>();
                try { await vendaService.CancelarAsync(venda.Id); } catch { /* ignore */ }
            }
        }
    }
}
