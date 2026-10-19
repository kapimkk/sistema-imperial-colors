using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Constants;
using ImperialColors.UI.Services;
using ImperialColors.UI.ViewModels;
using ImperialColors.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// O botão de filtro de unidade da tela de estoque, ao lado da barra de pesquisa. Construir a
/// tela de verdade valida o XAML — StaticResource que não resolve e binding para propriedade
/// inexistente só estouram em tempo de execução.
/// </summary>
public class EstoqueFiltroUnidadeViewTests
{
    public EstoqueFiltroUnidadeViewTests() => WpfTestBootstrap.Inicializar();

    private sealed class StoreFixo(params string[] marcadas) : IFiltroUnidadeEstoqueStore
    {
        public IReadOnlyList<string> Carregar() => marcadas;
        public void Salvar(IReadOnlyCollection<string> unidades) { }
    }

    // Filtro já marcado vem do armazenamento, sem disparar busca: o teste de tela não depende
    // do carregamento em segundo plano do ViewModel.
    private static (EstoqueView Tela, ProdutoViewModel ViewModel, Window Janela) Abrir(params string[] marcadas)
    {
        var viewModel = new ProdutoViewModel(
            Mock.Of<IProdutoService>(), Mock.Of<IServiceScopeFactory>(), new StoreFixo(marcadas));
        var tela = new EstoqueView(viewModel);

        // Os bindings só se ligam com a tela dentro de uma janela mostrada.
        var janela = new Window { Content = tela, Width = 1400, Height = 700 };
        janela.Show();
        janela.UpdateLayout();
        return (tela, viewModel, janela);
    }

    private static void Bombear(int milissegundos = 150)
    {
        var fim = DateTime.UtcNow.AddMilliseconds(milissegundos);
        while (DateTime.UtcNow < fim)
        {
            var quadro = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => quadro.Continue = false));
            Dispatcher.PushFrame(quadro);
            Thread.Sleep(10);
        }
    }

    [StaFact]
    public void Tela_TemOBotaoDeFiltroAoLadoDaBusca_ComTodasAsUnidades()
    {
        var (tela, _, janela) = Abrir();

        var botao = Assert.IsType<ToggleButton>(tela.FindName("BtnFiltroUnidade"));
        var lista = Assert.IsType<ItemsControl>(tela.FindName("ListaUnidades"));
        var busca = Assert.IsType<TextBox>(tela.FindName("TxtBuscaEstoque"));

        // Ao lado da busca: na mesma linha de filtros, na coluna logo depois dela.
        Assert.Equal(Grid.GetRow(busca.Parent as FrameworkElement ?? busca), Grid.GetRow((FrameworkElement)botao.Parent));
        Assert.True(Grid.GetColumn((FrameworkElement)botao.Parent) > Grid.GetColumn((FrameworkElement)busca.Parent));

        Assert.Equal(UnidadesMedida.Todas.Length, lista.Items.Count);
        Assert.Contains("Unidade", Assert.IsType<TextBlock>(botao.Content).Text);

        janela.Close();
    }

    [StaFact]
    public void SemFiltro_OBotaoLimparFiltroFicaEscondido()
    {
        var (tela, _, janela) = Abrir();

        var limpar = Assert.IsType<Button>(tela.FindName("BtnLimparFiltroUnidade"));

        Assert.Equal(Visibility.Collapsed, limpar.Visibility);

        janela.Close();
    }

    /// <summary>Filtro vindo do armazenamento (a visita anterior): a tela já abre com ele ligado,
    /// o botão diz qual é e o "Limpar filtro" aparece.</summary>
    [StaFact]
    public void ComFiltroGuardado_TelaAbreFiltrada_ELimparFiltroAparece()
    {
        var (tela, viewModel, janela) = Abrir("BD");

        var botao = Assert.IsType<ToggleButton>(tela.FindName("BtnFiltroUnidade"));
        var limpar = Assert.IsType<Button>(tela.FindName("BtnLimparFiltroUnidade"));

        Assert.Equal(Visibility.Visible, limpar.Visibility);
        Assert.Contains("BD", Assert.IsType<TextBlock>(botao.Content).Text);
        Assert.True(viewModel.OpcoesUnidade.Single(o => o.Sigla == "BD").Selecionada);

        janela.Close();
    }

    [StaFact]
    public void Cabecalho_MostraOResumoDoFiltro()
    {
        var (tela, _, janela) = Abrir("BD");

        var resumo = Assert.IsType<TextBlock>(tela.FindName("TxtResumoTotal"));

        Assert.Contains("unidade BD", resumo.Text);

        janela.Close();
    }

    /// <summary>Clicar em "Limpar filtro" na tela de verdade: o botão some e o texto volta ao
    /// padrão.</summary>
    [StaFact]
    public void ClicarEmLimparFiltro_DesligaOFiltroNaTela()
    {
        var (tela, viewModel, janela) = Abrir("BD", "GL");
        var limpar = Assert.IsType<Button>(tela.FindName("BtnLimparFiltroUnidade"));
        var botao = Assert.IsType<ToggleButton>(tela.FindName("BtnFiltroUnidade"));

        limpar.Command.Execute(null);
        Bombear(); // a busca em segundo plano pede a interface; deixa ela responder
        janela.UpdateLayout();

        Assert.Equal(Visibility.Collapsed, limpar.Visibility);
        Assert.Equal("⏷  Unidade", Assert.IsType<TextBlock>(botao.Content).Text.Trim());
        Assert.False(viewModel.FiltroUnidadeAtivo);

        janela.Close();
    }

    /// <summary>Marcar uma opção na lista (como o clique em um CheckBox) liga o filtro.</summary>
    [StaFact]
    public void MarcarUmaOpcaoNaLista_LigaOFiltro()
    {
        var (tela, viewModel, janela) = Abrir();
        var limpar = Assert.IsType<Button>(tela.FindName("BtnLimparFiltroUnidade"));

        viewModel.OpcoesUnidade.Single(o => o.Sigla == "LA").Selecionada = true;
        Bombear();
        janela.UpdateLayout();

        Assert.Equal(Visibility.Visible, limpar.Visibility);
        Assert.Contains("LA", Assert.IsType<TextBlock>(((ToggleButton)tela.FindName("BtnFiltroUnidade")).Content).Text);

        janela.Close();
    }
}
