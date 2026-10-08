using ImperialColors.Application.DTOs;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Entities;
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
/// Cálculo de totais fiscais (ICMSTot/IBSCBSTot equivalentes) ponta a ponta: cadastra
/// produto com tributação, registra venda, e confere que o serviço soma os itens
/// corretamente. Requer RUN_INTEGRATION_TESTS=true e PostgreSQL acessível via .env.
/// </summary>
[Collection(ConfiguracaoFiscalGlobalCollection.Nome)]
public class CalculoFiscalVendaServiceIntegrationTests
{
    [Fact]
    public async Task CalcularAsync_VendaComProdutoTributado_CalculaTotaisCorretamente()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var cs))
            return;

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(cs);
        services.AddApplication();

        await using var provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        await context.Database.MigrateAsync();

        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var produtoService = sp.GetRequiredService<IProdutoService>();
        var categoriaRepo = sp.GetRequiredService<IRepository<Categoria>>();
        var marcaRepo = sp.GetRequiredService<IRepository<Marca>>();
        var vendaService = sp.GetRequiredService<IVendaService>();
        var configuracaoFiscal = sp.GetRequiredService<IConfiguracaoFiscalService>();
        var calculoFiscal = sp.GetRequiredService<ICalculoFiscalVendaService>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var categoria = await categoriaRepo.AdicionarAsync(new Categoria { Nome = $"CatCalculo{sufixo}", Ativo = true });
        var marca = await marcaRepo.AdicionarAsync(new Marca { Nome = $"MarcaCalculo{sufixo}", Ativo = true });

        var produto = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            CodigoInterno = await produtoService.GerarProximoCodigoInternoAsync(),
            Nome = $"ProdutoCalculo{sufixo}",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            Custo = 5m,
            PrecoVenda = 100m,
            QuantidadeEstoque = 10,
            EstoqueMinimo = 0,
            Unidade = "UN"
        });

        // Regime padrão do sistema é Simples Nacional — CSOSN 102 é o caso comum, sem
        // destaque de ICMS. PIS/COFINS "07" (não tributado) também é comum nesse regime.
        await produtoService.SalvarTributacaoAsync(produto.Id, new TributacaoProdutoDto
        {
            Ncm = "32089000",
            CsosnIcms = "102",
            CstPis = "07",
            CstCofins = "07"
        });

        await configuracaoFiscal.SalvarConfiguracaoEmpresaAsync(new ConfiguracaoFiscalEmpresaDto
        {
            AliquotaIbsUfPadrao = 0.10m,
            AliquotaIbsMunicipioPadrao = 0.00m,
            AliquotaCbsPadrao = 0.90m
        });

        var venda = await vendaService.CriarAsync(new CriarVendaDto
        {
            ConsumidorFinal = true,
            FormaPagamento = FormaPagamento.Dinheiro,
            ValorPago = 200m,
            Itens = [new CriarItemVendaDto { ProdutoId = produto.Id, Quantidade = 2, PrecoUnitario = 100m }]
        });

        try
        {
            var totais = await calculoFiscal.CalcularAsync(venda.Id);

            Assert.Equal(venda.Id, totais.VendaId);
            Assert.Equal(200m, totais.VProd);
            Assert.Equal(200m, totais.VNf);

            // CSOSN 102 + PIS/COFINS "07": nada destacado, sem avisos de ICMS/PIS/COFINS.
            Assert.Equal(0m, totais.VIcms);
            Assert.Equal(0m, totais.VPis);
            Assert.Equal(0m, totais.VCofins);
            Assert.DoesNotContain(totais.Avisos, a => a.Contains("ICMS") || a.Contains("PIS") || a.Contains("COFINS"));

            // IBS/CBS sobre R$200: 0,10% = 0,20 e 0,90% = 1,80.
            Assert.Equal(0.20m, totais.VIbsUf);
            Assert.Equal(0.00m, totais.VIbsMunicipio);
            Assert.Equal(0.20m, totais.VIbs);
            Assert.Equal(1.80m, totais.VCbs);

            Assert.Single(totais.Itens);
        }
        finally
        {
            await vendaService.CancelarAsync(venda.Id);
            await configuracaoFiscal.SalvarConfiguracaoEmpresaAsync(new ConfiguracaoFiscalEmpresaDto());
        }
    }

    [Fact]
    public async Task CalcularAsync_ProdutoSemTributacaoCadastrada_GeraAvisoENaoQuebra()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var cs))
            return;

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(cs);
        services.AddApplication();

        await using var provider = services.BuildServiceProvider();
        using var context = provider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
        await context.Database.MigrateAsync();

        await using var scope = provider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var produtoService = sp.GetRequiredService<IProdutoService>();
        var categoriaRepo = sp.GetRequiredService<IRepository<Categoria>>();
        var marcaRepo = sp.GetRequiredService<IRepository<Marca>>();
        var vendaService = sp.GetRequiredService<IVendaService>();
        var calculoFiscal = sp.GetRequiredService<ICalculoFiscalVendaService>();

        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var categoria = await categoriaRepo.AdicionarAsync(new Categoria { Nome = $"CatSemTrib{sufixo}", Ativo = true });
        var marca = await marcaRepo.AdicionarAsync(new Marca { Nome = $"MarcaSemTrib{sufixo}", Ativo = true });

        var produto = await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            CodigoInterno = await produtoService.GerarProximoCodigoInternoAsync(),
            Nome = $"ProdutoSemTrib{sufixo}",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            Custo = 5m,
            PrecoVenda = 50m,
            QuantidadeEstoque = 10,
            EstoqueMinimo = 0,
            Unidade = "UN"
        });
        // Não chama SalvarTributacaoAsync de propósito — produto fica sem tributação.

        var venda = await vendaService.CriarAsync(new CriarVendaDto
        {
            ConsumidorFinal = true,
            FormaPagamento = FormaPagamento.Dinheiro,
            ValorPago = 50m,
            Itens = [new CriarItemVendaDto { ProdutoId = produto.Id, Quantidade = 1, PrecoUnitario = 50m }]
        });

        try
        {
            var totais = await calculoFiscal.CalcularAsync(venda.Id);

            Assert.Contains(totais.Avisos, a => a.Contains("sem tributação cadastrada"));
            Assert.Equal(0m, totais.VIcms);
        }
        finally
        {
            await vendaService.CancelarAsync(venda.Id);
        }
    }
}
