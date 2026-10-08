using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// M9 da auditoria de 15/09 (caixa-branca): a movimentação de estoque inicial vai no mesmo
/// objeto entregue ao repositório, e se a gravação do produto falha nada fica gravado à parte.
/// </summary>
public class ProdutoEstoqueInicialTests
{
    private readonly Mock<IProdutoRepository> _produtoRepository = new();
    private readonly Mock<IRepository<Categoria>> _categoriaRepository = new();
    private readonly Mock<IRepository<Marca>> _marcaRepository = new();

    public ProdutoEstoqueInicialTests()
    {
        _categoriaRepository.Setup(r => r.ExisteAsync(1)).ReturnsAsync(true);
        _marcaRepository.Setup(r => r.ExisteAsync(1)).ReturnsAsync(true);
    }

    private ProdutoService CriarService() => new(
        _produtoRepository.Object, _categoriaRepository.Object, _marcaRepository.Object,
        Mock.Of<ITributacaoProdutoRepository>(), Mock.Of<IConfiguracaoFiscalService>(),
        Mock.Of<IAuditoriaService>(), new UsuarioAtualSistema(), NullLogger<ProdutoService>.Instance);

    private static CriarProdutoDto Dto(decimal estoque) => new()
    {
        PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
        Nome = "Tinta Acrílica 18L",
        CodigoInterno = "TA001",
        CodigoInternoDefinidoManualmente = true,
        CategoriaId = 1,
        MarcaId = 1,
        PrecoVenda = 100m,
        QuantidadeEstoque = estoque,
        EstoqueMinimo = 0
    };

    [Fact]
    public async Task ComEstoque_EntregaProdutoEMovimentacaoInicialNumaUnicaGravacao()
    {
        Produto? recebido = null;
        _produtoRepository
            .Setup(r => r.InserirProdutoAsync(It.IsAny<Produto>(), It.IsAny<bool>(), It.IsAny<Func<Task<string>>>()))
            .Callback<Produto, bool, Func<Task<string>>>((p, _, _) => recebido = p)
            .ReturnsAsync((Produto p, bool _, Func<Task<string>> _) => p);

        await CriarService().CriarAsync(Dto(12m));

        var movimentacao = Assert.Single(recebido!.Movimentacoes);
        Assert.Equal(TipoMovimentacao.Entrada, movimentacao.Tipo);
        Assert.Equal(12m, movimentacao.QuantidadeAtual);
        Assert.Equal("Estoque inicial", movimentacao.Motivo);
    }

    [Fact]
    public async Task SemEstoque_NaoCriaMovimentacao()
    {
        Produto? recebido = null;
        _produtoRepository
            .Setup(r => r.InserirProdutoAsync(It.IsAny<Produto>(), It.IsAny<bool>(), It.IsAny<Func<Task<string>>>()))
            .Callback<Produto, bool, Func<Task<string>>>((p, _, _) => recebido = p)
            .ReturnsAsync((Produto p, bool _, Func<Task<string>> _) => p);

        await CriarService().CriarAsync(Dto(0m));

        Assert.Empty(recebido!.Movimentacoes);
    }

    [Fact]
    public async Task FalhaAoGravarOProduto_NaoDeixaNenhumaGravacaoSeparada()
    {
        _produtoRepository
            .Setup(r => r.InserirProdutoAsync(It.IsAny<Produto>(), It.IsAny<bool>(), It.IsAny<Func<Task<string>>>()))
            .ThrowsAsync(new DomainException("Erro real do banco: falha simulada"));

        // O serviço não tem mais repositório de movimentação: a única gravação é a do produto, que falhou.
        await Assert.ThrowsAsync<DomainException>(() => CriarService().CriarAsync(Dto(5m)));
        _produtoRepository.Verify(r => r.InserirProdutoAsync(It.IsAny<Produto>(), It.IsAny<bool>(), It.IsAny<Func<Task<string>>>()), Times.Once);
    }
}
