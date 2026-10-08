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
/// PDV / venda com "comprador avulso" (sem cliente cadastrado, só nome + documento pro
/// cupom): o CPF/CNPJ digitado precisa ter dígito verificador válido — evita gravar um
/// documento errado que só seria descoberto na hora de emitir a nota fiscal. Requer
/// RUN_INTEGRATION_TESTS=true e PostgreSQL acessível via .env.
/// </summary>
public class IdentificacaoCompradorAvulsoIntegrationTests
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

    private static async Task<ProdutoDto> CriarProdutoTesteAsync(IServiceProvider sp, string sufixo)
    {
        var produtoService = sp.GetRequiredService<IProdutoService>();
        var categoriaRepo = sp.GetRequiredService<IRepository<Categoria>>();
        var marcaRepo = sp.GetRequiredService<IRepository<Marca>>();

        var categoria = await categoriaRepo.AdicionarAsync(new Categoria { Nome = $"CatAvulso{sufixo}", Ativo = true });
        var marca = await marcaRepo.AdicionarAsync(new Marca { Nome = $"MarcaAvulso{sufixo}", Ativo = true });

        return await produtoService.CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            CodigoInterno = await produtoService.GerarProximoCodigoInternoAsync(),
            Nome = $"ProdutoAvulso{sufixo}",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            Custo = 5m,
            PrecoVenda = 10m,
            QuantidadeEstoque = 10,
            EstoqueMinimo = 0,
            Unidade = "UN"
        });
    }

    [Fact]
    public async Task CriarAsync_CompradorAvulsoComCpfInvalido_LancaDomainException()
    {
        if (!TryCarregarConfig(out var provider))
            return;

        await using var scope = provider.CreateAsyncScope();
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var produto = await CriarProdutoTesteAsync(scope.ServiceProvider, sufixo);
        var vendaService = scope.ServiceProvider.GetRequiredService<IVendaService>();

        var dto = new CriarVendaDto
        {
            ConsumidorFinal = false,
            NomeCompradorAvulso = "Comprador Teste",
            DocumentoCompradorAvulso = "111.444.777-34", // dígito verificador errado
            TipoPessoaCompradorAvulso = TipoPessoa.Fisica,
            FormaPagamento = FormaPagamento.Dinheiro,
            ValorPago = 10m,
            Itens = [new CriarItemVendaDto { ProdutoId = produto.Id, Quantidade = 1, PrecoUnitario = 10m }]
        };

        var ex = await Assert.ThrowsAsync<DomainException>(() => vendaService.CriarAsync(dto));
        Assert.Contains("CPF", ex.Message);
    }

    [Fact]
    public async Task CriarAsync_CompradorAvulsoComCpfValido_CriaVendaComSucesso()
    {
        if (!TryCarregarConfig(out var provider))
            return;

        await using var scope = provider.CreateAsyncScope();
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var produto = await CriarProdutoTesteAsync(scope.ServiceProvider, sufixo);
        var vendaService = scope.ServiceProvider.GetRequiredService<IVendaService>();

        var dto = new CriarVendaDto
        {
            ConsumidorFinal = false,
            NomeCompradorAvulso = "Comprador Teste",
            DocumentoCompradorAvulso = "111.444.777-35", // válido
            TipoPessoaCompradorAvulso = TipoPessoa.Fisica,
            FormaPagamento = FormaPagamento.Dinheiro,
            ValorPago = 10m,
            Itens = [new CriarItemVendaDto { ProdutoId = produto.Id, Quantidade = 1, PrecoUnitario = 10m }]
        };

        var venda = await vendaService.CriarAsync(dto);
        Assert.Equal("111.444.777-35", venda.DocumentoCompradorExibicao);

        await vendaService.CancelarAsync(venda.Id);
    }

    [Fact]
    public async Task CriarAsync_CompradorAvulsoComCnpjInvalido_LancaDomainException()
    {
        if (!TryCarregarConfig(out var provider))
            return;

        await using var scope = provider.CreateAsyncScope();
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var produto = await CriarProdutoTesteAsync(scope.ServiceProvider, sufixo);
        var vendaService = scope.ServiceProvider.GetRequiredService<IVendaService>();

        var dto = new CriarVendaDto
        {
            ConsumidorFinal = false,
            NomeCompradorAvulso = "Empresa Teste LTDA",
            DocumentoCompradorAvulso = "11.222.333/0001-80", // dígito verificador errado
            TipoPessoaCompradorAvulso = TipoPessoa.Juridica,
            FormaPagamento = FormaPagamento.Dinheiro,
            ValorPago = 10m,
            Itens = [new CriarItemVendaDto { ProdutoId = produto.Id, Quantidade = 1, PrecoUnitario = 10m }]
        };

        var ex = await Assert.ThrowsAsync<DomainException>(() => vendaService.CriarAsync(dto));
        Assert.Contains("CNPJ", ex.Message);
    }
}
