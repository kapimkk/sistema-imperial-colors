using System.Drawing;
using System.Drawing.Imaging;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Application.Validation;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

public sealed class ProdutoFreteImagemTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), $"imperial_catalog_image_test_{Guid.NewGuid():N}");
    private readonly ImagemProdutoStorage _storage;
    public ProdutoFreteImagemTests()
    {
        Directory.CreateDirectory(_raiz);
        _storage = new ImagemProdutoStorage(_raiz);
    }

    [Theory]
    [InlineData("5,500", 5500)]
    [InlineData("0.001", 1)]
    [InlineData("25", 25000)]
    public void PesoKg_ConverteSemArredondar(string texto, int gramas)
    {
        Assert.True(PesoProdutoHelper.TentarLerQuilos(texto, out var real));
        Assert.Equal(gramas, real);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1,0001")]
    [InlineData("1.000,500")]
    [InlineData("NaN")]
    [InlineData("99999999999999999999999999999")]
    public void PesoKg_InvalidoNaoViraPesoFicticio(string texto)
        => Assert.False(PesoProdutoHelper.TentarLerQuilos(texto, out _));

    [Theory]
    [InlineData("25,00", "25")]
    [InlineData("0.01", "0.01")]
    public void DimensaoCm_DecimalExato(string texto, string esperado)
    {
        Assert.True(PesoProdutoHelper.TentarLerCentimetros(texto, out var valor));
        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), valor);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-20")]
    [InlineData("25.001")]
    [InlineData("1.000,00")]
    [InlineData("100000000")]
    public void DimensaoInvalida_NaoTruncaNemArredonda(string texto)
        => Assert.False(PesoProdutoHelper.TentarLerCentimetros(texto, out _));

    [Fact]
    public void Novo_RequerDadosDeFrete_LegadoPermaneceEditavel()
    {
        var dto = Dto();
        dto.PesoGramas = null;
        dto.AlturaCm = dto.LarguraCm = dto.ComprimentoCm = null;
        ProdutoValidator.Validar(dto); // Atualização legado: sem fictícios, continua pendente.
        Assert.Throws<DomainException>(() => ProdutoValidator.ValidarNovo(dto));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DimensaoPreenchidaZeroOuNegativa_Recusada(decimal valor)
    {
        var dto = Dto();
        dto.AlturaCm = valor;
        Assert.Throws<DomainException>(() => ProdutoValidator.Validar(dto));
    }

    [Fact]
    public void Observacoes_ExcessoRecusadoSemTruncamento()
    {
        var dto = Dto();
        dto.Observacoes = new string('a', 10001);
        Assert.Throws<DomainException>(() => ProdutoValidator.Validar(dto));
    }

    [Fact]
    public async Task Criar_PreservaObservacoesQuebrasEDecimais()
    {
        var repo = Repo();
        var dto = Dto();
        dto.Observacoes = "Linha 1\nLinha 2 & <texto>";
        var result = await Servico(repo).CriarAsync(dto);
        Assert.Equal(dto.Observacoes, result.Observacoes);
        Assert.Equal(5500, result.PesoGramas);
        Assert.Equal(25.25m, result.AlturaCm);
        Assert.Equal(20.10m, result.LarguraCm);
        Assert.Equal(30.99m, result.ComprimentoCm);
    }

    [Fact]
    public async Task Imagem_CriaPastaEReferenciaRelativaSemUsarNomePerigoso()
    {
        var arquivo = CriarPng("SKU estranho;..produto.png");
        var referencia = await _storage.ImportarAsync(arquivo);
        Assert.Matches(@"^ImagensProdutos/[a-f0-9]{32}\.png$", referencia);
        Assert.True(File.Exists(_storage.ObterCaminhoSeguro(referencia)));
        Assert.DoesNotContain("SKU", referencia);
    }

    [Fact]
    public async Task Imagem_NomesDuplicadosNaoColidem()
    {
        var arquivo = CriarPng("imagem.png");
        var a = await _storage.ImportarAsync(arquivo);
        var b = await _storage.ImportarAsync(arquivo);
        Assert.NotEqual(a, b);
        Assert.True(File.Exists(_storage.ObterCaminhoSeguro(a)));
        Assert.True(File.Exists(_storage.ObterCaminhoSeguro(b)));
    }

    [Theory]
    [InlineData("../fora.png")]
    [InlineData("ImagensProdutos/../fora.png")]
    [InlineData("C:/Windows/a.png")]
    [InlineData(@"ImagensProdutos\a.png")]
    [InlineData("ImagensProdutos/a.exe")]
    public void Imagem_PathTraversalOuReferenciaExternaRecusada(string referencia)
        => Assert.Throws<DomainException>(() => _storage.ObterCaminhoSeguro(referencia));

    [Fact]
    public void Imagem_AusenteNaoFabricaArquivo()
    {
        var referencia = $"ImagensProdutos/{Guid.NewGuid():N}.png";
        Assert.False(File.Exists(_storage.ObterCaminhoSeguro(referencia)));
        _storage.RemoverSeExistir(referencia);
    }

    [Fact]
    public void Imagem_ExecutavelRenomeadoRecusado()
    {
        var arquivo = Path.Combine(_raiz, "programa.png");
        File.WriteAllText(arquivo, "MZ executable payload");
        Assert.Throws<DomainException>(() => _storage.ValidarArquivo(arquivo));
    }

    [Fact]
    public void Imagem_CorrompidaComAssinaturaRecusada()
    {
        var arquivo = Path.Combine(_raiz, "corrompida.png");
        File.WriteAllBytes(arquivo, new byte[] {137,80,78,71,13,10,26,10,1,2,3,4});
        Assert.Throws<DomainException>(() => _storage.ValidarArquivo(arquivo));
    }

    [Fact]
    public void Imagem_ExtensaoDiferenteDoConteudoRecusada()
    {
        var png = CriarPng("real.png");
        var jpg = Path.Combine(_raiz, "real.jpg");
        File.Copy(png, jpg);
        Assert.Throws<DomainException>(() => _storage.ValidarArquivo(jpg));
    }

    [Fact]
    public void Imagem_AcimaDeCincoMilhoesBytesRecusada()
    {
        var arquivo = Path.Combine(_raiz, "grande.png");
        using (var stream = File.Create(arquivo)) stream.SetLength(5_000_001);
        Assert.Throws<DomainException>(() => _storage.ValidarArquivo(arquivo));
    }

    [Fact]
    public async Task Substituicao_RemoveAntigaSomenteAposCommit()
    {
        var antigo = await _storage.ImportarAsync(CriarPng("antiga.png"));
        var produto = ProdutoAntigo(antigo);
        var repo = Repo(produto);
        repo.Setup(r => r.AtualizarComAjusteEstoqueTransacionalAsync(It.IsAny<Produto>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback(() => Assert.True(File.Exists(_storage.ObterCaminhoSeguro(antigo))))
            .ReturnsAsync((Produto p, decimal _, decimal _, string _, string _, CancellationToken _, bool _) => p);
        var dto = Atualizacao();
        dto.AlterarImagem = true;
        dto.ImagemArquivoSelecionado = CriarPng("nova.png");
        var salvo = await Servico(repo).AtualizarAsync(7, dto);
        Assert.NotEqual(antigo, salvo.ImagemProdutoPath);
        Assert.False(File.Exists(_storage.ObterCaminhoSeguro(antigo)));
        Assert.True(File.Exists(_storage.ObterCaminhoSeguro(salvo.ImagemProdutoPath)));
        Assert.False(salvo.ImagemRemovida);
    }

    [Fact]
    public async Task Substituicao_RollbackPreservaAntigaELimpaNova()
    {
        var antigo = await _storage.ImportarAsync(CriarPng("antiga.png"));
        var repo = Repo(ProdutoAntigo(antigo));
        repo.Setup(r => r.AtualizarComAjusteEstoqueTransacionalAsync(It.IsAny<Produto>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ThrowsAsync(new DomainException("Banco local recusou a gravação."));
        var dto = Atualizacao();
        dto.AlterarImagem = true;
        dto.ImagemArquivoSelecionado = CriarPng("nova.png");
        await Assert.ThrowsAsync<DomainException>(() => Servico(repo).AtualizarAsync(7, dto));
        Assert.True(File.Exists(_storage.ObterCaminhoSeguro(antigo)));
        Assert.Single(Directory.GetFiles(Path.Combine(_raiz, ImagemProdutoStorage.Pasta)));
    }

    [Fact]
    public async Task Remocao_ExplicitaGravaMarcadorENaoRemoveAntesDeCommit()
    {
        var antigo = await _storage.ImportarAsync(CriarPng("antiga.png"));
        var repo = Repo(ProdutoAntigo(antigo));
        var dto = Atualizacao();
        dto.RemoverImagem = true;
        var salvo = await Servico(repo).AtualizarAsync(7, dto);
        Assert.Null(salvo.ImagemProdutoPath);
        Assert.True(salvo.ImagemRemovida);
        Assert.False(File.Exists(_storage.ObterCaminhoSeguro(antigo)));
    }

    [Fact]
    public async Task EditarSemImagemSelecionada_NaoTransformaAusenciaEmRemocao()
    {
        var referencia = $"ImagensProdutos/{Guid.NewGuid():N}.png";
        var repo = Repo(ProdutoAntigo(referencia));
        var dto = Atualizacao();
        dto.PesoGramas = null;
        dto.AlturaCm = dto.LarguraCm = dto.ComprimentoCm = null;
        var salvo = await Servico(repo).AtualizarAsync(7, dto);
        Assert.Equal(referencia, salvo.ImagemProdutoPath);
        Assert.False(salvo.ImagemRemovida);
        Assert.Null(salvo.PesoGramas);
    }

    [Fact]
    public async Task Imagem_JpegValidoImportaENormalizaExtensao()
    {
        var arquivo = Path.Combine(_raiz,"produto.jpeg");
        using (var bitmap = new Bitmap(2,2)) bitmap.Save(arquivo,ImageFormat.Jpeg);
        var referencia = await _storage.ImportarAsync(arquivo);
        Assert.EndsWith(".jpg",referencia);
        Assert.True(File.Exists(_storage.ObterCaminhoSeguro(referencia)));
    }

    [Fact]
    public void Imagem_DimensaoAcimaDeOitoMilPixelsRecusada()
    {
        var arquivo = Path.Combine(_raiz,"larga.png");
        using (var bitmap = new Bitmap(8001,1)) bitmap.Save(arquivo,ImageFormat.Png);
        Assert.Throws<DomainException>(() => _storage.ValidarArquivo(arquivo));
    }

    [Fact]
    public async Task Criacao_RollbackLimpaImagemPreparadaENaoApagaOrigem()
    {
        var repo = Repo();
        repo.Setup(r => r.InserirProdutoAsync(It.IsAny<Produto>(),It.IsAny<bool>(),It.IsAny<Func<Task<string>>>()))
            .ThrowsAsync(new DomainException("Falha fictícia de gravação."));
        var dto = Dto();
        dto.AlterarImagem = true;
        dto.ImagemArquivoSelecionado = CriarPng("origem.png");
        await Assert.ThrowsAsync<DomainException>(() => Servico(repo).CriarAsync(dto));
        Assert.True(File.Exists(dto.ImagemArquivoSelecionado));
        Assert.Empty(Directory.GetFiles(Path.Combine(_raiz,ImagemProdutoStorage.Pasta)));
    }

    private string CriarPng(string nome)
    {
        var arquivo = Path.Combine(_raiz, nome);
        using var bitmap = new Bitmap(2, 2);
        bitmap.SetPixel(0, 0, Color.Red);
        bitmap.Save(arquivo, ImageFormat.Png);
        return arquivo;
    }
    private static CriarProdutoDto Dto() => new()
    {
        CodigoInterno = "TEST001", Nome = "Produto exclusivamente de teste",
        CategoriaId = 1, MarcaId = 1, PrecoVenda = 50m, PesoGramas = 5500,
        AlturaCm = 25.25m, LarguraCm = 20.10m, ComprimentoCm = 30.99m
    };
    private static AtualizarProdutoDto Atualizacao() => new()
    {
        CodigoInterno = "TEST001", Nome = "Produto exclusivamente de teste",
        CategoriaId = 1, MarcaId = 1, PrecoVenda = 50m, PesoGramas = 5500,
        AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m
    };
    private static Produto ProdutoAntigo(string referencia) => new()
    {
        Id = 7, CodigoInterno = "TEST001", Nome = "Produto exclusivamente de teste",
        ImagemProdutoPath = referencia
    };
    private static Mock<IProdutoRepository> Repo(Produto? produto = null)
    {
        var repo = new Mock<IProdutoRepository>();
        repo.Setup(r => r.InserirProdutoAsync(It.IsAny<Produto>(), It.IsAny<bool>(), It.IsAny<Func<Task<string>>>()))
            .ReturnsAsync((Produto p, bool _, Func<Task<string>> _) => p);
        repo.Setup(r => r.ObterPorIdAsync(7)).ReturnsAsync(produto);
        repo.Setup(r => r.AtualizarComAjusteEstoqueTransacionalAsync(It.IsAny<Produto>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync((Produto p, decimal _, decimal _, string _, string _, CancellationToken _, bool _) => p);
        return repo;
    }
    private ProdutoService Servico(Mock<IProdutoRepository> repo)
    {
        var categorias = new Mock<IRepository<Categoria>>();
        categorias.Setup(r => r.ExisteAsync(1)).ReturnsAsync(true);
        var marcas = new Mock<IRepository<Marca>>();
        marcas.Setup(r => r.ExisteAsync(1)).ReturnsAsync(true);
        return new ProdutoService(repo.Object, categorias.Object, marcas.Object,
            Mock.Of<ITributacaoProdutoRepository>(), Mock.Of<IConfiguracaoFiscalService>(),
            Mock.Of<IAuditoriaService>(), new UsuarioAtualSistema(), NullLogger<ProdutoService>.Instance, _storage);
    }

    public void Dispose()
    {
        var caminho = Path.GetFullPath(_raiz);
        if (caminho.StartsWith(Path.Combine(Path.GetTempPath(), "imperial_catalog_image_test_"), StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(caminho) && (File.GetAttributes(caminho) & FileAttributes.ReparsePoint) == 0)
            Directory.Delete(caminho, recursive: true);
    }
}