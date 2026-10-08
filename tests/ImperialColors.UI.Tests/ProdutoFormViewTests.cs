using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.UI.Helpers;
using ImperialColors.UI.Views;
using Moq;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace ImperialColors.UI.Tests;

public class FormattingHelperTests
{
    public FormattingHelperTests()
    {
        FormattingHelper.ConfigurarCulturaThread();
    }

    [Fact]
    public void FormatarMoeda_DeveUsarRealBrasileiro()
    {
        Assert.Equal("R$ 45,50", FormattingHelper.FormatarMoeda(45.50m));
        Assert.Equal("R$ 89,90", FormattingHelper.FormatarMoeda(89.90m));
    }

    [Fact]
    public void TryParseMoeda_DeveInterpretarFormatoBrasileiro()
    {
        Assert.True(FormattingHelper.TryParseMoeda("R$ 45,50", out var valor));
        Assert.Equal(45.50m, valor);
    }

    [Theory]
    [InlineData(10, "UN", "10 Unidades")]
    [InlineData(1, "UN", "1 Unidade")]
    [InlineData(3.6, "LT", "3,6 Litros")]
    [InlineData(2, "CX", "2 Caixas")]
    public void FormatarQuantidadeUnidade_DeveRetornarTextoAmigavel(decimal qtd, string unidade, string esperado)
    {
        Assert.Equal(esperado, FormattingHelper.FormatarQuantidadeUnidade(qtd, unidade));
    }

    [Fact]
    public void FormatarDataHora_DeveUsarPadraoBrasileiro()
    {
        var data = new DateTime(2026, 6, 10, 14, 30, 0);
        Assert.Equal("10/06/2026 14:30", FormattingHelper.FormatarDataHora(data));
    }
}

public class ProdutoFormViewTests
{
    public ProdutoFormViewTests()
    {
        WpfTestBootstrap.Inicializar();
    }

    [StaFact]
    public void ProdutoFormView_AbrirEFecharCincoVezesSemExcecao()
    {
        for (var i = 0; i < 5; i++)
        {
            var excecao = Record.Exception(() =>
            {
                var form = CriarForm();
                form.InicializarNovo();
                form.InicializarEdicao(CriarProdutoExemplo());
                form.Close();
            });

            Assert.Null(excecao);
        }
    }

    [StaFact]
    public void ProdutoFormView_ComboBoxesNaoUsamDisplayMemberPathComItemTemplate()
    {
        var form = CriarForm();

        form.InicializarNovo();
        form.Show();

        try
        {
            var categoria = form.FindName("CmbCategoria") as ComboBox;
            var marca = form.FindName("CmbMarca") as ComboBox;

            Assert.NotNull(categoria);
            Assert.NotNull(marca);
            Assert.True(string.IsNullOrEmpty(categoria!.DisplayMemberPath));
            Assert.True(string.IsNullOrEmpty(marca!.DisplayMemberPath));
            Assert.NotNull(categoria.ItemTemplate);
            Assert.NotNull(marca.ItemTemplate);
        }
        finally
        {
            form.Close();
        }
    }

    [StaFact]
    public void ProdutoFormView_EdicaoExibeMoedaFormatada()
    {
        var form = CriarForm();

        form.InicializarEdicao(CriarProdutoExemplo());

        Assert.Equal("R$ 45,50", (form.FindName("TxtCusto") as TextBox)?.Text);
        Assert.Equal("R$ 89,90", (form.FindName("TxtPrecoVenda") as TextBox)?.Text);
        form.Close();
    }

    [StaFact]
    public void ProdutoFormView_ComboBoxesNaoContemPlaceholderIdZero()
    {
        var form = CriarForm();

        form.InicializarNovo();
        form.Show();

        try
        {
            var categoria = form.FindName("CmbCategoria") as ComboBox;
            var marca = form.FindName("CmbMarca") as ComboBox;

            Assert.All(categoria!.Items.Cast<CategoriaDto>(), c => Assert.True(c.Id > 0));
            Assert.All(marca!.Items.Cast<MarcaDto>(), m => Assert.True(m.Id > 0));
        }
        finally
        {
            form.Close();
        }
    }

    /// <summary>O antigo <c>CmbLitragemGl</c> (dropdown fixo 3,6L/18L, só para Galão) foi
    /// substituído por um campo de texto livre, para qualquer unidade — confirma que o
    /// campo novo existe, aceita edição/limpeza, e que o dropdown antigo não sobrou.</summary>
    [StaFact]
    public void ProdutoFormView_TamanhoEmbalagemEhTextoLivrePreenchidoNaEdicaoELimpoNoNovo()
    {
        var form = CriarForm();

        Assert.Null(form.FindName("CmbLitragemGl"));
        Assert.Null(form.FindName("PainelLitragemGl"));

        var produto = CriarProdutoExemplo();
        produto.Unidade = "BA";
        produto.TamanhoEmbalagem = "25 KG";
        form.InicializarEdicao(produto);

        var campo = form.FindName("TxtTamanhoEmbalagem") as TextBox;
        Assert.NotNull(campo);
        Assert.Equal("25 KG", campo!.Text);

        form.InicializarNovo();
        Assert.Equal(string.Empty, campo.Text);

        form.Close();
    }

    /// <summary>O peso é guardado em gramas, mas conferido em quilos — o eco ao lado do
    /// campo é o que impede um zero a mais (55000) de passar batido. Cobre também a volta
    /// ao formulário em branco, para o peso do produto anterior não ficar na tela.</summary>
    [StaFact]
    public void ProdutoFormView_PesoEmQuilosPreservaGramasExatos()
    {
        var form = CriarForm();

        var produto = CriarProdutoExemplo();
        produto.PesoGramas = 5500;
        form.InicializarEdicao(produto);

        var campo = form.FindName("TxtPesoKg") as TextBox;
        var eco = form.FindName("TxtPesoEquivalente") as TextBlock;
        Assert.NotNull(campo);
        Assert.NotNull(eco);
        Assert.Equal("5,500", campo!.Text);
        Assert.Equal("= 5500 g", eco!.Text);
        Assert.Equal(Visibility.Visible, eco.Visibility);

        campo.Text = "0,800";
        Assert.Equal("= 800 g", eco.Text);

        // Texto que não é peso não ecoa nada — quem barra de fato é a validação ao salvar.
        campo.Text = "abc";
        Assert.Equal(Visibility.Collapsed, eco.Visibility);

        form.InicializarNovo();
        Assert.Equal(string.Empty, campo.Text);
        Assert.Equal(Visibility.Collapsed, eco.Visibility);

        form.Close();
    }

    [StaFact]
    public void ProdutoFormView_FreteExibeUnidadesEDimensoesExatas()
    {
        var form = CriarForm();
        var produto = CriarProdutoExemplo();
        produto.PesoGramas = 5500;
        produto.AlturaCm = 25.25m;
        produto.LarguraCm = 20.10m;
        produto.ComprimentoCm = 30.99m;
        form.InicializarEdicao(produto);
        Assert.Equal("5,500", ((TextBox)form.FindName("TxtPesoKg")).Text);
        Assert.Equal("25,25", ((TextBox)form.FindName("TxtAlturaCm")).Text);
        Assert.Equal("20,10", ((TextBox)form.FindName("TxtLarguraCm")).Text);
        Assert.Equal("30,99", ((TextBox)form.FindName("TxtComprimentoCm")).Text);
        Assert.Equal("Pronto para frete", ((TextBlock)form.FindName("TxtFretePendente")).Text);
        ((TextBox)form.FindName("TxtAlturaCm")).Text = "0";
        Assert.Equal("Dados de frete pendentes", ((TextBlock)form.FindName("TxtFretePendente")).Text);
        form.Close();
    }

    [StaFact]
    public void ProdutoFormView_LegadoSemDimensoesNaoRecebeValoresFicticios()
    {
        var form = CriarForm();
        form.InicializarEdicao(CriarProdutoExemplo());
        Assert.Equal(string.Empty, ((TextBox)form.FindName("TxtPesoKg")).Text);
        Assert.Equal(string.Empty, ((TextBox)form.FindName("TxtAlturaCm")).Text);
        Assert.Equal(string.Empty, ((TextBox)form.FindName("TxtLarguraCm")).Text);
        Assert.Equal(string.Empty, ((TextBox)form.FindName("TxtComprimentoCm")).Text);
        Assert.Equal("Dados de frete pendentes", ((TextBlock)form.FindName("TxtFretePendente")).Text);
        Assert.Contains("existente", ((TextBlock)form.FindName("TxtRegraFrete")).Text);
        form.InicializarNovo();
        Assert.Contains("Obrigatórios", ((TextBlock)form.FindName("TxtRegraFrete")).Text);
        form.Close();
    }

    [StaFact]
    public void ProdutoFormView_ObservacoesPreservaTextoEQuebras()
    {
        var form = CriarForm();
        var produto = CriarProdutoExemplo();
        produto.Observacoes = "Descrição & <texto>\n" + new string('a',600);
        form.InicializarEdicao(produto);
        Assert.Equal(produto.Observacoes,((TextBox)form.FindName("TxtObservacoes")).Text);
        form.Close();
    }

    [StaFact]
    public void ProdutoFormView_ImagemAusenteMostraPendenciaESemRemocaoAutomatica()
    {
        var form = CriarForm();
        var produto = CriarProdutoExemplo();
        produto.ImagemProdutoPath = "ImagensProdutos/11223344556677889900aabbccddeeff.png";
        form.InicializarEdicao(produto);
        Assert.Contains("não encontrado",((TextBlock)form.FindName("TxtImagemEstado")).Text);
        Assert.True(((Button)form.FindName("BtnRemoverImagem")).IsEnabled);
        Assert.Null(((Image)form.FindName("ImgProduto")).Source);
        ((Button)form.FindName("BtnRemoverImagem")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("ao salvar",((TextBlock)form.FindName("TxtImagemEstado")).Text);
        Assert.False(((Button)form.FindName("BtnRemoverImagem")).IsEnabled);
        form.Close();
    }
    [StaFact]
    public void ProdutoFormView_PreviewNaoTravaArquivoERemocaoAguardaSalvar()
    {
        var raiz = System.IO.Path.Combine(System.IO.Path.GetTempPath(),"imperial_ui_image_test_"+Guid.NewGuid().ToString("N"));
        var imagens = System.IO.Path.Combine(raiz,"ImagensProdutos");
        System.IO.Directory.CreateDirectory(imagens);
        var referencia = "ImagensProdutos/"+Guid.NewGuid().ToString("N")+".png";
        var arquivo = System.IO.Path.Combine(raiz,referencia);
        using (var bitmap = new System.Drawing.Bitmap(2,2)) bitmap.Save(arquivo,System.Drawing.Imaging.ImageFormat.Png);
        var form = new ProdutoFormView(CriarProdutoServiceMock().Object,CriarCategoriaServiceMock().Object,
            CriarMarcaServiceMock().Object,CriarFornecedorServiceMock().Object,
            Mock.Of<IConfiguracaoFiscalService>(),Mock.Of<INcmService>(),
            new ImperialColors.Infrastructure.Services.ImagemProdutoStorage(raiz));
        try
        {
            var produto = CriarProdutoExemplo();
            produto.ImagemProdutoPath = referencia;
            form.InicializarEdicao(produto);
            Assert.NotNull(((Image)form.FindName("ImgProduto")).Source);
            Assert.Equal("Alterar imagem",((Button)form.FindName("BtnSelecionarImagem")).Content);
            using (var semLock = new System.IO.FileStream(arquivo,System.IO.FileMode.Open,System.IO.FileAccess.ReadWrite,System.IO.FileShare.None))
                Assert.True(semLock.Length > 0);
            ((Button)form.FindName("BtnRemoverImagem")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(((Image)form.FindName("ImgProduto")).Source);
            Assert.True(System.IO.File.Exists(arquivo));
            Assert.Contains("ao salvar",((TextBlock)form.FindName("TxtImagemEstado")).Text);
        }
        finally
        {
            form.Close();
            var caminho = System.IO.Path.GetFullPath(raiz);
            if(caminho.StartsWith(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"imperial_ui_image_test_"),StringComparison.OrdinalIgnoreCase)
                && (System.IO.File.GetAttributes(caminho) & System.IO.FileAttributes.ReparsePoint)==0)
                System.IO.Directory.Delete(caminho,true);
        }
    }
    private static Mock<IProdutoService> CriarProdutoServiceMock()
    {
        var mock = new Mock<IProdutoService>();
        mock.Setup(s => s.GerarProximoCodigoInternoAsync()).ReturnsAsync("P00001");
        mock.Setup(s => s.GerarCodigoInternoPorNomeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("MCC001");
        mock.Setup(s => s.CodigoBarrasExisteAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        return mock;
    }

    private static Mock<ICategoriaService> CriarCategoriaServiceMock()
    {
        var mock = new Mock<ICategoriaService>();
        mock.Setup(s => s.ObterTodosAsync()).ReturnsAsync(new List<CategoriaDto>
        {
            new() { Id = 1, Nome = "Tintas Acrílicas" }
        });
        mock.Setup(s => s.CriarAsync(It.IsAny<string>())).ReturnsAsync(new CategoriaDto { Id = 2, Nome = "Nova" });
        return mock;
    }

    private static Mock<IMarcaService> CriarMarcaServiceMock()
    {
        var mock = new Mock<IMarcaService>();
        mock.Setup(s => s.ObterTodosAsync()).ReturnsAsync(new List<MarcaDto>
        {
            new() { Id = 1, Nome = "Suvinil" }
        });
        mock.Setup(s => s.CriarAsync(It.IsAny<string>())).ReturnsAsync(new MarcaDto { Id = 2, Nome = "Nova" });
        return mock;
    }

    private static Mock<IFornecedorService> CriarFornecedorServiceMock()
    {
        var mock = new Mock<IFornecedorService>();
        mock.Setup(s => s.ObterTodosAsync()).ReturnsAsync(new List<FornecedorDto>());
        return mock;
    }

    private static ProdutoFormView CriarForm()
        => new(
            CriarProdutoServiceMock().Object,
            CriarCategoriaServiceMock().Object,
            CriarMarcaServiceMock().Object,
            CriarFornecedorServiceMock().Object,
            Mock.Of<IConfiguracaoFiscalService>(),
            Mock.Of<INcmService>());

    private static ProdutoDto CriarProdutoExemplo() => new()
    {
        Id = 1,
        CodigoInterno = "P00001",
        Nome = "Tinta Branca",
        Custo = 45.50m,
        PrecoVenda = 89.90m,
        QuantidadeEstoque = 10,
        EstoqueMinimo = 2,
        Unidade = "LT",
        CategoriaId = 1,
        MarcaId = 1
    };
}
