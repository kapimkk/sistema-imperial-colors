using System.Reflection;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

public class TributacaoProdutoMapeamentoTests
{
    [Theory]
    [InlineData(RegimeTributario.SimplesNacional)]
    [InlineData(RegimeTributario.LucroPresumido)]
    public async Task SalvarEObter_TransportaTodosOsCamposEntreDtoEEntidade(RegimeTributario regime)
    {
        TributacaoProduto? armazenada = null;
        var repository = new Mock<ITributacaoProdutoRepository>();
        repository.Setup(r => r.SalvarAsync(It.IsAny<TributacaoProduto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TributacaoProduto tributacao, CancellationToken _) => armazenada = tributacao);
        repository.Setup(r => r.ObterPorProdutoIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => armazenada);
        var produtos = new Mock<IProdutoRepository>();
        produtos.Setup(r => r.ExisteAsync(42)).ReturnsAsync(true);
        var servico = DadosTributacaoProduto.CriarServico(produtos.Object, repository.Object, regime);
        var esperado = DadosTributacaoProduto.Completa(42, regime);

        var salvo = await servico.SalvarTributacaoAsync(42, esperado);
        var reaberto = await servico.ObterTributacaoAsync(42);

        DadosTributacaoProduto.Comparar(esperado, salvo);
        DadosTributacaoProduto.Comparar(esperado, reaberto);
    }
}

/// <summary>Exercita INSERT e UPDATE reais, incluindo leitura por um contexto novo.
/// Só executa com opt-in e conexão local de teste aprovada por IntegrationTestGuard.</summary>
[Collection(SemParalelismoCollection.Nome)]
public class TributacaoProdutoPersistenciaIntegrationTests
{
    private sealed class Fabrica(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }

    private sealed class Cenario(Fabrica fabrica, int produtoId, ProdutoService servico) : IAsyncDisposable
    {
        public int ProdutoId => produtoId;
        public ProdutoService Servico => servico;

        public async Task ConferirAsync(TributacaoProdutoDto esperado)
        {
            var salvo = await Servico.SalvarTributacaoAsync(ProdutoId, esperado);
            DadosTributacaoProduto.Comparar(esperado, salvo);
            var reaberto = await Servico.ObterTributacaoAsync(ProdutoId);
            DadosTributacaoProduto.Comparar(esperado, reaberto);
            await using var contexto = fabrica.CreateDbContext();
            var entidade = await contexto.TributacoesProduto.AsNoTracking().SingleAsync(t => t.ProdutoId == ProdutoId);
            Assert.NotNull(entidade.AtualizadoEm);
            Assert.Equal(1, await contexto.TributacoesProduto.CountAsync(t => t.ProdutoId == ProdutoId));
        }

        public async ValueTask DisposeAsync()
        {
            await using var contexto = fabrica.CreateDbContext();
            await contexto.Produtos.Where(p => p.Id == ProdutoId).ExecuteDeleteAsync();
        }
    }

    private static async Task<Cenario?> CriarAsync(RegimeTributario regime)
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var connectionString)) return null;
        var fabrica = new Fabrica(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        await using var contexto = fabrica.CreateDbContext();
        await contexto.Database.MigrateAsync();
        var sufixo = Guid.NewGuid().ToString("N");
        var produto = new Produto { CodigoInterno = $"TRIB-{sufixo}", Nome = $"Tributação de teste {sufixo}", Unidade = "UN" };
        contexto.Produtos.Add(produto);
        await contexto.SaveChangesAsync();
        var servico = DadosTributacaoProduto.CriarServico(new ProdutoRepository(fabrica), new TributacaoProdutoRepository(fabrica), regime);
        return new Cenario(fabrica, produto.Id, servico);
    }

    [Theory]
    [InlineData(RegimeTributario.SimplesNacional)]
    [InlineData(RegimeTributario.LucroPresumido)]
    public async Task InserirAtualizarELimparTributacao_PreservaTodosOsCamposAoReabrir(RegimeTributario regime)
    {
        await using var cenario = await CriarAsync(regime);
        if (cenario is null) return;

        await cenario.ConferirAsync(DadosTributacaoProduto.Completa(cenario.ProdutoId, regime));
        await cenario.ConferirAsync(DadosTributacaoProduto.Completa(cenario.ProdutoId, regime, atualizada: true));
        await cenario.ConferirAsync(new TributacaoProdutoDto { ProdutoId = cenario.ProdutoId });
    }

    [Fact]
    public async Task AliquotaStRetido_GravaAtualizacaoDeNuloParaValorEParaZero()
    {
        await using var cenario = await CriarAsync(RegimeTributario.SimplesNacional);
        if (cenario is null) return;
        var tributacao = new TributacaoProdutoDto { ProdutoId = cenario.ProdutoId, CsosnIcms = "500" };
        await cenario.ConferirAsync(tributacao);
        tributacao.AliquotaIcmsStRetido = 18.75m;
        await cenario.ConferirAsync(tributacao);
        tributacao.AliquotaIcmsStRetido = 0m;
        await cenario.ConferirAsync(tributacao);
        tributacao.AliquotaIcmsStRetido = null;
        await cenario.ConferirAsync(tributacao);
    }

    [Theory]
    [InlineData(RegimeTributario.SimplesNacional)]
    [InlineData(RegimeTributario.LucroPresumido)]
    public async Task PadraoDaCategoria_InsereAtualizaELimpaTodosOsCamposCompartilhados(RegimeTributario regime)
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var connectionString)) return;
        var fabrica = new Fabrica(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
        await using var contexto = fabrica.CreateDbContext();
        await contexto.Database.MigrateAsync();
        var categoria = new Categoria { Nome = $"Tributação de teste {Guid.NewGuid():N}" };
        contexto.Categorias.Add(categoria);
        await contexto.SaveChangesAsync();
        try
        {
            var configuracao = new Mock<IConfiguracaoFiscalService>();
            configuracao.Setup(c => c.ObterRegimeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(regime);
            var servico = new CategoriaService(new RepositoryBase<Categoria>(fabrica),
                new TributacaoCategoriaRepository(fabrica), configuracao.Object, NullLogger<CategoriaService>.Instance);
            var perfis = new[]
            {
                TributacaoCategoriaDto.DoProdutoDto(categoria.Id, DadosTributacaoProduto.Completa(0, regime)),
                TributacaoCategoriaDto.DoProdutoDto(categoria.Id, DadosTributacaoProduto.Completa(0, regime, atualizada: true)),
                new TributacaoCategoriaDto { CategoriaId = categoria.Id }
            };
            foreach (var perfil in perfis)
            {
                var salvo = await servico.SalvarTributacaoPadraoAsync(categoria.Id, perfil);
                var reaberto = await servico.ObterTributacaoPadraoAsync(categoria.Id);
                DadosTributacaoProduto.Comparar(perfil.ParaProdutoDto(0), salvo.ParaProdutoDto(0));
                DadosTributacaoProduto.Comparar(perfil.ParaProdutoDto(0), reaberto.ParaProdutoDto(0));
            }
        }
        finally
        {
            await contexto.Categorias.Where(c => c.Id == categoria.Id).ExecuteDeleteAsync();
        }
    }
}

internal static class DadosTributacaoProduto
{
    private static readonly PropertyInfo[] Campos = typeof(TributacaoProdutoDto).GetProperties().Where(p => p.CanWrite).ToArray();

    internal static ProdutoService CriarServico(IProdutoRepository produtos, ITributacaoProdutoRepository tributacao, RegimeTributario regime)
    {
        var configuracao = new Mock<IConfiguracaoFiscalService>();
        configuracao.Setup(c => c.ObterRegimeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(regime);
        return new ProdutoService(produtos, Mock.Of<IRepository<Categoria>>(), Mock.Of<IRepository<Marca>>(),
            tributacao, configuracao.Object, Mock.Of<IAuditoriaService>(), Mock.Of<IUsuarioAtual>(), NullLogger<ProdutoService>.Instance);
    }

    internal static TributacaoProdutoDto Completa(int produtoId, RegimeTributario regime, bool atualizada = false)
    {
        var normal = regime is RegimeTributario.LucroPresumido or RegimeTributario.LucroReal;
        var dto = new TributacaoProdutoDto
        {
            ProdutoId = produtoId,
            Ncm = atualizada ? "32081010" : "32089000",
            Cest = atualizada ? "2400200" : "2400100",
            Origem = atualizada ? OrigemMercadoria.EstrangeiraMercadoInterno : OrigemMercadoria.Nacional,
            CstIcms = normal ? (atualizada ? "10" : "60") : null,
            CsosnIcms = normal ? null : (atualizada ? "201" : "500"),
            AliquotaIcms = atualizada ? 19.25m : 18.50m,
            AliquotaIcmsSt = atualizada ? 20.25m : 19.50m,
            AliquotaIcmsStRetido = atualizada ? 22.75m : 21.25m,
            Mva = atualizada ? 50.25m : 40.50m,
            ReducaoBaseCalculo = atualizada ? 15.25m : 10.50m,
            CstPis = atualizada ? "02" : "01",
            AliquotaPis = atualizada ? 2m : 1.65m,
            CstCofins = atualizada ? "02" : "01",
            AliquotaCofins = atualizada ? 8.25m : 7.60m,
            CstIpi = atualizada ? "99" : "50",
            CodigoEnquadramentoIpi = atualizada ? "998" : "999",
            AliquotaIpi = atualizada ? 6.25m : 5.50m,
            ValorIpiFixo = atualizada ? 3.25m : 2.50m,
            ExTipi = atualizada ? "002" : "001",
            UnidadeTributavel = atualizada ? "KG" : "LT",
            FatorConversao = atualizada ? 2.3456m : 1.2345m,
            GtinTributavel = atualizada ? "7891234567895" : "SEM GTIN",
            CfopDentroEstado = atualizada ? "5405" : "5102",
            CfopForaEstado = atualizada ? "6404" : "6102",
            CstIbsCbs = atualizada ? "200" : "000",
            CClassTrib = atualizada ? "200001" : "000001",
            CstIS = atualizada ? "200" : "000",
            CClassTribIS = atualizada ? "200002" : "000002",
            AliquotaIS = atualizada ? 4.25m : 3.50m,
            AliquotaIbsMunicipioDiferimento = atualizada ? 20.25m : 10.50m,
            AliquotaIbsMunicipioReducao = atualizada ? 40.25m : 30.50m
        };
        // Qualquer campo novo deve receber valor nesta fixture para não escapar do roundtrip.
        foreach (var campo in Campos.Where(p => p.Name is not (nameof(dto.CstIcms) or nameof(dto.CsosnIcms))))
            Assert.NotNull(campo.GetValue(dto));
        return dto;
    }

    internal static void Comparar(TributacaoProdutoDto esperado, TributacaoProdutoDto atual)
    {
        foreach (var campo in Campos)
            Assert.True(Equals(campo.GetValue(esperado), campo.GetValue(atual)),
                $"Campo {campo.Name}: esperado '{campo.GetValue(esperado) ?? "null"}', recebido '{campo.GetValue(atual) ?? "null"}'.");
    }
}
