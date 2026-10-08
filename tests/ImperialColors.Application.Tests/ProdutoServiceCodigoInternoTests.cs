using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

public class ProdutoServiceCodigoInternoTests
{
    private readonly Mock<IProdutoRepository> _produtoRepository = new();
    private readonly Mock<IRepository<Categoria>> _categoriaRepository = new();
    private readonly Mock<IRepository<Marca>> _marcaRepository = new();

    public ProdutoServiceCodigoInternoTests()
    {
        _categoriaRepository.Setup(r => r.ExisteAsync(1)).ReturnsAsync(true);
        _marcaRepository.Setup(r => r.ExisteAsync(1)).ReturnsAsync(true);
        _produtoRepository.Setup(r => r.ObterMaiorSequenciaCodigoInternoAsync()).ReturnsAsync(10);
    }

    [Fact]
    public async Task CriarAsync_CodigoManualDuplicado_DeveLancarMensagemAmigavel()
    {
        _produtoRepository.Setup(r => r.CodigoInternoExisteAsync("P00005")).ReturnsAsync(true);

        var service = CriarService();
        var dto = CriarDtoValido("P00005", codigoManual: true);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CriarAsync(dto));
        Assert.Equal("Este código interno já está em uso por outro produto.", ex.Message);
        _produtoRepository.Verify(r => r.InserirProdutoAsync(It.IsAny<Produto>(), It.IsAny<bool>(), It.IsAny<Func<Task<string>>>()), Times.Never);
    }

    [Fact]
    public async Task CriarAsync_CodigoAutomaticoDuplicado_DeveRegenerarAntesDeInserir()
    {
        _produtoRepository.Setup(r => r.CodigoInternoExisteAsync("P00011")).ReturnsAsync(true);
        _produtoRepository.Setup(r => r.ObterMaiorSequenciaCodigoInternoAsync()).ReturnsAsync(11);
        _produtoRepository
            .Setup(r => r.InserirProdutoAsync(It.IsAny<Produto>(), true, It.IsAny<Func<Task<string>>>()))
            .ReturnsAsync((Produto p, bool _, Func<Task<string>> __) => p);

        var service = CriarService();
        var dto = CriarDtoValido("P00011", codigoManual: false);

        await service.CriarAsync(dto);

        _produtoRepository.Verify(r => r.InserirProdutoAsync(
            It.Is<Produto>(p => p.CodigoInterno == "P00012"),
            true,
            It.IsAny<Func<Task<string>>>()), Times.Once);
    }

    [Fact]
    public async Task GerarProximoCodigoInternoAsync_DeveUsarMaiorSequenciaDoBanco()
    {
        _produtoRepository.Setup(r => r.ObterMaiorSequenciaCodigoInternoAsync()).ReturnsAsync(25);

        var service = CriarService();
        var codigo = await service.GerarProximoCodigoInternoAsync();

        Assert.Equal("P00026", codigo);
    }

    [Fact]
    public async Task GerarCodigoInternoPorNomeAsync_DeveUsarSiglaESequencial()
    {
        _produtoRepository
            .Setup(r => r.ObterMaiorSequenciaPorSiglaAsync("MCC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var service = CriarService();
        var codigo = await service.GerarCodigoInternoPorNomeAsync("Massa Corrida Coral");

        Assert.Equal("MCC003", codigo);
    }

    private ProdutoService CriarService() => new(
        _produtoRepository.Object,
        _categoriaRepository.Object,
        _marcaRepository.Object,
        Mock.Of<ITributacaoProdutoRepository>(),
        Mock.Of<IConfiguracaoFiscalService>(),
        Mock.Of<IAuditoriaService>(),
        new UsuarioAtualSistema(),
        NullLogger<ProdutoService>.Instance);

    private static CriarProdutoDto CriarDtoValido(string codigo, bool codigoManual) => new()
    {
        PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
        CodigoInterno = codigo,
        CodigoInternoDefinidoManualmente = codigoManual,
        Nome = "Produto Teste",
        CategoriaId = 1,
        MarcaId = 1,
        Custo = 10m,
        PrecoVenda = 20m,
        QuantidadeEstoque = 1,
        EstoqueMinimo = 0,
        Unidade = "UN"
    };
}
