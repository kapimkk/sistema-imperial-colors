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

public class ProdutoExclusaoCodigoBarrasTests
{
    private readonly Mock<IProdutoRepository> _produtoRepository = new();
    private readonly Mock<IRepository<Categoria>> _categoriaRepository = new();
    private readonly Mock<IRepository<Marca>> _marcaRepository = new();

    public ProdutoExclusaoCodigoBarrasTests()
    {
        _categoriaRepository.Setup(r => r.ExisteAsync(1)).ReturnsAsync(true);
        _marcaRepository.Setup(r => r.ExisteAsync(1)).ReturnsAsync(true);
    }

    [Fact]
    public async Task RemoverAsync_ComHistoricoComercial_DeveBloquearExclusao()
    {
        _produtoRepository.Setup(r => r.ObterPorIdAsync(10))
            .ReturnsAsync(new Produto { Id = 10, Nome = "Tinta X" });
        _produtoRepository.Setup(r => r.PossuiHistoricoComercialAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CriarService();

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.RemoverAsync(10));

        Assert.Equal(ProdutoService.MensagemExclusaoBloqueadaPorHistorico, ex.Message);
        _produtoRepository.Verify(r => r.RemoverFisicamenteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoverAsync_SemHistorico_DeveExecutarHardDelete()
    {
        _produtoRepository.Setup(r => r.ObterPorIdAsync(11))
            .ReturnsAsync(new Produto { Id = 11, Nome = "Produto Teste" });
        _produtoRepository.Setup(r => r.PossuiHistoricoComercialAsync(11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _produtoRepository.Setup(r => r.ExisteFisicamenteAsync(11, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var service = CriarService();
        await service.RemoverAsync(11);

        _produtoRepository.Verify(r => r.RemoverFisicamenteAsync(11, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CodigoBarrasExisteAsync_CodigoVazio_DeveRetornarFalse()
    {
        var service = CriarService();

        var existe = await service.CodigoBarrasExisteAsync("   ");

        Assert.False(existe);
        _produtoRepository.Verify(r => r.CodigoBarrasExisteAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CriarAsync_CodigoBarrasDuplicado_DeveLancarDomainException()
    {
        _produtoRepository.Setup(r => r.CodigoInternoExisteAsync(It.IsAny<string>())).ReturnsAsync(false);
        _produtoRepository.Setup(r => r.CodigoBarrasExisteAsync("7891234567890", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CriarService();
        var dto = CriarDtoValido();
        dto.CodigoBarras = "7891234567890";

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.CriarAsync(dto));

        Assert.Equal(ProdutoService.MensagemCodigoBarrasDuplicado, ex.Message);
        _produtoRepository.Verify(r => r.InserirProdutoAsync(It.IsAny<Produto>(), It.IsAny<bool>(), It.IsAny<Func<Task<string>>>()), Times.Never);
    }

    [Fact]
    public async Task AtualizarAsync_CodigoBarrasDeOutroProduto_DeveLancarDomainException()
    {
        _produtoRepository.Setup(r => r.ObterPorIdAsync(5))
            .ReturnsAsync(new Produto { Id = 5, CodigoInterno = "P00005", Nome = "A", QuantidadeEstoque = 1 });
        _produtoRepository.Setup(r => r.CodigoInternoExisteAsync("P00005", 5)).ReturnsAsync(false);
        _produtoRepository.Setup(r => r.CodigoBarrasExisteAsync("7891234567890", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CriarService();
        var dto = CriarDtoAtualizacao();
        dto.CodigoBarras = "7891234567890";

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.AtualizarAsync(5, dto));

        Assert.Equal(ProdutoService.MensagemCodigoBarrasDuplicado, ex.Message);
        _produtoRepository.Verify(r => r.AtualizarAsync(It.IsAny<Produto>()), Times.Never);
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

    private static CriarProdutoDto CriarDtoValido() => new()
    {
        PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
        CodigoInterno = "P00099",
        Nome = "Produto QA",
        CategoriaId = 1,
        MarcaId = 1,
        PrecoVenda = 10m,
        QuantidadeEstoque = 0,
        Unidade = "UN"
    };

    private static AtualizarProdutoDto CriarDtoAtualizacao() => new()
    {
        Id = 5,
        CodigoInterno = "P00005",
        Nome = "Produto QA",
        CategoriaId = 1,
        MarcaId = 1,
        PrecoVenda = 10m,
        QuantidadeEstoque = 1,
        Unidade = "UN"
    };
}
