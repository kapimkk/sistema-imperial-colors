using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

public class ProdutoPaginacaoTests
{
    [Fact]
    public async Task ObterPaginadoAsync_DeveRetornarMetadadosCorretos()
    {
        var produtoRepository = new Mock<IProdutoRepository>();
        produtoRepository
            .Setup(r => r.ObterPaginadoAsync(2, 50, null, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Produto>
            {
                new() { Id = 1, Nome = "Produto A", CodigoInterno = "P00001", Custo = 1, PrecoVenda = 2, Unidade = "UN" }
            }, 120));

        var service = new ProdutoService(
            produtoRepository.Object,
            Mock.Of<IRepository<Categoria>>(),
            Mock.Of<IRepository<Marca>>(),
            Mock.Of<ITributacaoProdutoRepository>(),
            Mock.Of<IConfiguracaoFiscalService>(),
            Mock.Of<IAuditoriaService>(),
            new UsuarioAtualSistema(),
            NullLogger<ProdutoService>.Instance);

        var resultado = await service.ObterPaginadoAsync(2, 50);

        Assert.Equal(2, resultado.PaginaAtual);
        Assert.Equal(50, resultado.ItensPorPagina);
        Assert.Equal(120, resultado.TotalItens);
        Assert.Equal(3, resultado.TotalPaginas);
        Assert.Single(resultado.Itens);
    }
}
