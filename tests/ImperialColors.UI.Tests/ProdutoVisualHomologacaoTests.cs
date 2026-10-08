using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Enums;
using ImperialColors.Infrastructure.Services;
using ImperialColors.UI.Helpers;
using ImperialColors.UI.Views;
using Moq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>Renderiza o XAML real e exercita os mesmos handlers, sem banco, login ou diálogos externos.</summary>
public sealed class ProdutoVisualHomologacaoTests
{
    public ProdutoVisualHomologacaoTests() => WpfTestBootstrap.Inicializar();

    private sealed class Dialogos : IProdutoFormDialogs
    {
        public Queue<string?> Arquivos { get; } = new();
        public List<string> Mensagens { get; } = new();
        public string? SelecionarImagem(Window owner) => Arquivos.Count > 0 ? Arquivos.Dequeue() : null;
        public void MostrarValidacao(Window owner, string mensagem) => Mensagens.Add(mensagem);
    }

    private sealed class Tela : IDisposable
    {
        public string Raiz { get; } = Path.Combine(Path.GetTempPath(), "imperial-wpf-produto-" + Guid.NewGuid().ToString("N"));
        public Dialogos Dialogos { get; } = new();
        public Mock<IProdutoService> Produtos { get; } = new();
        public ProdutoFormView Form { get; }
        public Tela()
        {
            Directory.CreateDirectory(Raiz);
            Produtos.Setup(p => p.CodigoBarrasExisteAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Produtos.Setup(p => p.GerarCodigoInternoPorNomeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("VIS001");
            Produtos.Setup(p => p.ObterTributacaoAsync(It.IsAny<int>())).ReturnsAsync((TributacaoProdutoDto)null!);
            var categorias = new Mock<ICategoriaService>();
            categorias.Setup(c => c.ObterTodosAsync()).ReturnsAsync([new CategoriaDto { Id = 1, Nome = "Tintas" }]);
            var marcas = new Mock<IMarcaService>();
            marcas.Setup(c => c.ObterTodosAsync()).ReturnsAsync([new MarcaDto { Id = 1, Nome = "Marca de teste" }]);
            var fornecedores = new Mock<IFornecedorService>();
            fornecedores.Setup(c => c.ObterTodosAsync()).ReturnsAsync([]);
            var fiscal = new Mock<IConfiguracaoFiscalService>();
            fiscal.Setup(f => f.ObterRegimeAsync()).ReturnsAsync(RegimeTributario.SimplesNacional);
            Form = new ProdutoFormView(Produtos.Object, categorias.Object, marcas.Object, fornecedores.Object,
                fiscal.Object, Mock.Of<INcmService>(), new ImagemProdutoStorage(Raiz), Dialogos)
            {
                Width = 720,
                MaxHeight = Math.Min(SystemParameters.WorkArea.Height, 728),
                ShowActivated = false
            };
        }
        public T Campo<T>(string nome) where T : FrameworkElement => (T)Form.FindName(nome);
        public void Mostrar(ProdutoDto? produto = null)
        {
            if (produto is null) Form.InicializarNovo(); else Form.InicializarEdicao(produto);
            Form.Show();
            Form.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Form.UpdateLayout();
        }
        public void PreencherNovo()
        {
            Campo<TextBox>("TxtNome").Text = "Tinta de homologação visual";
            Campo<TextBox>("TxtCodigoInterno").Text = "VIS001";
            Campo<TextBox>("TxtPrecoVenda").Text = "89,90";
            Campo<TextBox>("TxtQuantidade").Text = "10";
            Campo<TextBox>("TxtEstoqueMinimo").Text = "2";
            Campo<ComboBox>("CmbCategoria").SelectedValue = 1;
            Campo<ComboBox>("CmbMarca").SelectedValue = 1;
        }
        public void Clicar(string nome) => Campo<Button>(nome).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        public void Capturar(string nome, string? foco = null)
        {
            if (foco == "ImgProduto") EncontrarScroll(Form)?.ScrollToBottom();
            else if (foco is not null) Campo<FrameworkElement>(foco).BringIntoView();
            Form.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Form.UpdateLayout();
            Assert.InRange(Form.ActualWidth, 700, 720);
            Assert.InRange(Form.ActualHeight, 700, 728);
            var botaoSalvar = Campo<Button>("BtnSalvar");
            var bounds = botaoSalvar.TransformToAncestor(Form).TransformBounds(new Rect(botaoSalvar.RenderSize));
            Assert.True(bounds.Bottom <= Form.ActualHeight && bounds.Top >= 0, "Rodapé de salvar deve permanecer visível.");
            var output = Environment.GetEnvironmentVariable("IMPERIAL_UI_VISUAL_OUTPUT_DIR");
            if (string.IsNullOrWhiteSpace(output)) return;
            Directory.CreateDirectory(output);
            var bitmap = new RenderTargetBitmap(1366, 768, 96, 96, PixelFormats.Pbgra32);
            var desenho = new DrawingVisual();
            using (var dc = desenho.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(230, 232, 235)), null, new Rect(0, 0, 1366, 768));
                dc.DrawRectangle(new VisualBrush(Form) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                    null, new Rect((1366 - Form.ActualWidth) / 2, 14, Form.ActualWidth, Form.ActualHeight));
            }
            bitmap.Render(desenho);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, nome + ".png"));
            png.Save(stream);
        }
        private static ScrollViewer? EncontrarScroll(DependencyObject root)
        {
            if (root is ScrollViewer scroll) return scroll;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
                if (EncontrarScroll(VisualTreeHelper.GetChild(root, index)) is { } found) return found;
            return null;
        }
        public string Imagem(string nome, System.Drawing.Color cor)
        {
            var arquivo = Path.Combine(Raiz, nome + ".png");
            using var bitmap = new System.Drawing.Bitmap(320, 240);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(cor);
            graphics.FillRectangle(System.Drawing.Brushes.White, 40, 70, 240, 100);
            bitmap.Save(arquivo, System.Drawing.Imaging.ImageFormat.Png);
            return arquivo;
        }
        public void Dispose()
        {
            Form.Close();
            var esperado = Path.GetFullPath(Path.GetTempPath());
            var alvo = Path.GetFullPath(Raiz);
            if (alvo.StartsWith(esperado, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(alvo).StartsWith("imperial-wpf-produto-", StringComparison.Ordinal)
                && (File.GetAttributes(alvo) & FileAttributes.ReparsePoint) == 0)
                Directory.Delete(alvo, true);
        }
    }

    private static ProdutoDto Legado() => new()
    {
        Id = 7, Nome = "Tinta legada — sem medidas", CodigoInterno = "LEG007", CategoriaId = 1, MarcaId = 1,
        Unidade = "LT", TamanhoEmbalagem = "3,6 L", PrecoVenda = 89.90m, Custo = 45.50m,
        QuantidadeEstoque = 10, EstoqueMinimo = 2, Observacoes = "Descrição preservada da loja.\nSegunda linha sem HTML."
    };

    [StaFact]
    public void Novo_RenderizaDescricaoFreteImagemERodapeEm1366x768()
    {
        using var t = new Tela();
        t.Mostrar();
        t.Capturar("01-novo-topo");
        Assert.Equal("Dados de frete pendentes", t.Campo<TextBlock>("TxtFretePendente").Text);
        Assert.Equal("Produto sem imagem", t.Campo<TextBlock>("TxtImagemEstado").Text);
        Assert.False(t.Campo<Button>("BtnRemoverImagem").IsEnabled);
        t.PreencherNovo();
        t.Campo<TextBox>("TxtObservacoes").Text = "Tinta acrílica de teste\nDescrição em duas linhas.";
        t.Campo<TextBox>("TxtPesoKg").Text = "5,500";
        t.Campo<TextBox>("TxtAlturaCm").Text = "25,00";
        t.Campo<TextBox>("TxtLarguraCm").Text = "20,00";
        t.Campo<TextBox>("TxtComprimentoCm").Text = "30,00";
        Assert.Equal("= 5500 g", t.Campo<TextBlock>("TxtPesoEquivalente").Text);
        Assert.Equal("Pronto para frete", t.Campo<TextBlock>("TxtFretePendente").Text);
        Assert.Same(t.Form.FindResource("VerdeSucessoBrush"), t.Campo<TextBlock>("TxtFretePendente").Foreground);
        t.Capturar("02-novo-descricao-frete", "TxtObservacoes");
        t.Capturar("03-novo-frete-imagem", "ImgProduto");
        var campos = new[] { "TxtPesoKg", "TxtAlturaCm", "TxtLarguraCm", "TxtComprimentoCm" };
        for (var i = 1; i < campos.Length; i++)
        {
            var anterior = t.Campo<TextBox>(campos[i - 1]);
            var atual = t.Campo<TextBox>(campos[i]);
            var a = anterior.TransformToAncestor(t.Form).TransformBounds(new Rect(anterior.RenderSize));
            var b = atual.TransformToAncestor(t.Form).TransformBounds(new Rect(atual.RenderSize));
            Assert.True(a.Right <= b.Left, "Campos de frete não podem se sobrepor.");
        }
    }

    [StaFact]
    public void LegadoSemMedidas_ContinuaEditavelEPreservadoAoSalvar()
    {
        using var t = new Tela();
        var original = Legado();
        AtualizarProdutoDto? enviado = null;
        t.Produtos.Setup(p => p.AtualizarAsync(It.IsAny<int>(), It.IsAny<AtualizarProdutoDto>()))
            .Callback<int, AtualizarProdutoDto>((_, dto) => enviado = dto).ReturnsAsync(original);
        t.Form.InicializarEdicao(original);
        t.Form.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            t.Form.UpdateLayout();
            t.Capturar("04-legado-sem-medidas", "TxtObservacoes");
            Assert.Equal("Dados de frete pendentes", t.Campo<TextBlock>("TxtFretePendente").Text);
            Assert.Equal("", t.Campo<TextBox>("TxtPesoKg").Text);
            t.Campo<TextBox>("TxtNome").Text = "Nome editado sem inventar medidas";
            t.Clicar("BtnSalvar");
        }));
        Assert.True(t.Form.ShowDialog());
        Assert.NotNull(enviado);
        Assert.Null(enviado!.PesoGramas);
        Assert.Null(enviado.AlturaCm);
        Assert.Null(enviado.LarguraCm);
        Assert.Null(enviado.ComprimentoCm);
        Assert.Equal(original.CodigoInterno, enviado.CodigoInterno);
        Assert.Equal(original.Observacoes, enviado.Observacoes);
        Assert.Equal(original.QuantidadeEstoque, enviado.QuantidadeEstoque);
        Assert.Empty(t.Dialogos.Mensagens);
    }

    [StaFact]
    public void NovoSemMedidas_MostraValidacaoSemChamarPersistencia()
    {
        using var t = new Tela();
        t.Mostrar(); t.PreencherNovo(); t.Clicar("BtnSalvar");
        Assert.Contains("Informe peso, altura, largura e comprimento", t.Dialogos.Mensagens.Single());
        Assert.Equal(Visibility.Visible, t.Campo<TextBlock>("TxtErroValidacao").Visibility);
        t.Produtos.Verify(p => p.CriarAsync(It.IsAny<CriarProdutoDto>()), Times.Never);
        t.Capturar("05-validacao-campos-obrigatorios", "TxtErroValidacao");
    }

    [StaTheory]
    [InlineData("TxtPesoKg", "0", "Peso inválido")]
    [InlineData("TxtPesoKg", "-1", "Peso inválido")]
    [InlineData("TxtPesoKg", "1,0001", "Peso inválido")]
    [InlineData("TxtAlturaCm", "0", "Dimensões inválidas")]
    [InlineData("TxtLarguraCm", "-1", "Dimensões inválidas")]
    [InlineData("TxtComprimentoCm", "25,001", "Dimensões inválidas")]
    public void FreteInvalido_ExibeMensagemSemPersistir(string campo, string valor, string esperado)
    {
        using var t = new Tela();
        t.Mostrar(); t.PreencherNovo();
        t.Campo<TextBox>("TxtPesoKg").Text = "5,500";
        t.Campo<TextBox>("TxtAlturaCm").Text = "25,00";
        t.Campo<TextBox>("TxtLarguraCm").Text = "20,00";
        t.Campo<TextBox>("TxtComprimentoCm").Text = "30,00";
        t.Campo<TextBox>(campo).Text = valor;
        Assert.Equal("Dados de frete pendentes", t.Campo<TextBlock>("TxtFretePendente").Text);
        t.Clicar("BtnSalvar");
        Assert.Contains(esperado, t.Dialogos.Mensagens.Single());
        t.Produtos.Verify(p => p.CriarAsync(It.IsAny<CriarProdutoDto>()), Times.Never);
    }

    [StaFact]
    public void Imagem_SelecionarSubstituirCancelarRemoverMantemArquivosAteSalvar()
    {
        using var t = new Tela();
        t.Mostrar(Legado());
        var primeira = t.Imagem("primeira", System.Drawing.Color.Red);
        var segunda = t.Imagem("segunda", System.Drawing.Color.Blue);
        t.Dialogos.Arquivos.Enqueue(primeira); t.Clicar("BtnSelecionarImagem");
        var preview1 = t.Campo<Image>("ImgProduto").Source;
        Assert.NotNull(preview1);
        Assert.Equal("Alterar imagem", t.Campo<Button>("BtnSelecionarImagem").Content);
        t.Capturar("06-preview-selecionada", "ImgProduto");
        t.Dialogos.Arquivos.Enqueue(segunda); t.Clicar("BtnSelecionarImagem");
        Assert.NotNull(t.Campo<Image>("ImgProduto").Source);
        Assert.NotSame(preview1, t.Campo<Image>("ImgProduto").Source);
        t.Capturar("07-preview-substituida", "ImgProduto");
        var preview2 = t.Campo<Image>("ImgProduto").Source;
        t.Dialogos.Arquivos.Enqueue(null); t.Clicar("BtnSelecionarImagem");
        Assert.Same(preview2, t.Campo<Image>("ImgProduto").Source);
        var invalida = Path.Combine(t.Raiz, "arquivo-invalido.png");
        File.WriteAllText(invalida, "Executável renomeado não é imagem");
        t.Dialogos.Arquivos.Enqueue(invalida); t.Clicar("BtnSelecionarImagem");
        Assert.Single(t.Dialogos.Mensagens);
        Assert.Same(preview2, t.Campo<Image>("ImgProduto").Source);
        t.Capturar("08-validacao-imagem-invalida", "TxtErroValidacao");
        t.Clicar("BtnRemoverImagem");
        Assert.Null(t.Campo<Image>("ImgProduto").Source);
        Assert.False(t.Campo<Button>("BtnRemoverImagem").IsEnabled);
        Assert.Contains("ao salvar", t.Campo<TextBlock>("TxtImagemEstado").Text);
        Assert.True(File.Exists(primeira)); Assert.True(File.Exists(segunda));
        t.Capturar("09-remocao-pendente", "ImgProduto");
    }
}
