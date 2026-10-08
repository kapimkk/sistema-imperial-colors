using System.Windows;
using System.Windows.Controls;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.UI.Services;
using ImperialColors.UI.ViewModels;
using ImperialColors.UI.Views;
using Moq;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// Aba Vendas do Dashboard: os dois blocos novos (itens por categoria e os 5 mais vendidos).
/// Construir a tela de verdade valida o XAML inteiro — StaticResource que não resolve e
/// binding para propriedade inexistente não aparecem na compilação e estourariam quando o
/// lojista abrisse a aba.
/// </summary>
public class DashboardVendasViewTests
{
    public DashboardVendasViewTests() => WpfTestBootstrap.Inicializar();

    private static (DashboardView Tela, DashboardViewModel ViewModel) Criar(DashboardProdutosVendidosDto produtos)
    {
        var servico = new Mock<IDashboardService>();
        servico.Setup(s => s.ObterVisaoVendasAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DashboardVendasDto());
        servico.Setup(s => s.ObterProdutosVendidosAsync(It.IsAny<PeriodoDashboard>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PeriodoDashboard p, CancellationToken _) =>
            {
                produtos.Periodo = p;
                return produtos;
            });
        return Criar(servico);
    }

    /// <summary>Constrói a tela de verdade em cima do serviço dado. Os testes de contagem de
    /// chamadas também precisam da tela: o ViewModel fora de uma janela deixa o dispatcher
    /// do teste sem bombear e o carregamento da segunda troca de período trava.</summary>
    private static (DashboardView Tela, DashboardViewModel ViewModel) Criar(Mock<IDashboardService> servico)
    {
        servico.Setup(s => s.ObterDadosDashboardAsync()).ReturnsAsync(new DashboardDto());

        var config = new Mock<IAppConfigService>();
        config.SetupGet(c => c.EmpresaNome).Returns("Imperial Colors");
        config.SetupGet(c => c.EmpresaSubtitulo).Returns("Tintas");

        var viewModel = new DashboardViewModel(servico.Object);
        return (new DashboardView(viewModel, config.Object), viewModel);
    }

    private static DashboardProdutosVendidosDto Vendas() => new()
    {
        ItensPorCategoria =
        [
            new CategoriaItensVendidosDto { Categoria = "Tintas", QuantidadeItens = 40m, PercentualBarra = 100m },
            new CategoriaItensVendidosDto { Categoria = "Pincéis", QuantidadeItens = 10m, PercentualBarra = 25m }
        ],
        ProdutosMaisVendidos =
        [
            new ProdutoMaisVendidoDto { Posicao = 1, CodigoInterno = "TAB-001", NomeProduto = "Tinta Branca", QuantidadeVendida = 40m, QuantidadeVendas = 12 }
        ]
    };

    [StaFact]
    public async Task AbaVendas_CarregaCategoriasETopCincoNoViewModel()
    {
        var (tela, viewModel) = Criar(Vendas());

        viewModel.TrocarVisaoCommand.Execute(VisaoDashboard.Vendas);
        // O carregamento é assíncrono por trás do comando.
        for (var i = 0; i < 50 && viewModel.ItensPorCategoria.Count == 0; i++)
            await Task.Delay(20);

        Assert.Equal(2, viewModel.ItensPorCategoria.Count);
        Assert.Equal("Tintas", viewModel.ItensPorCategoria[0].Categoria);
        Assert.Equal(12, viewModel.ProdutosMaisVendidosMes.Single().QuantidadeVendas);
        Assert.False(viewModel.SemItensVendidosNoMes);

        Assert.NotNull(tela);
    }

    /// <summary>Mês sem venda: os dois cards mostram a mensagem em vez de ficar vazios, que
    /// parece falha de carregamento.</summary>
    [StaFact]
    public async Task AbaVendas_SemItensVendidos_MarcaOEstadoVazio()
    {
        var (_, viewModel) = Criar(new DashboardProdutosVendidosDto());

        viewModel.TrocarVisaoCommand.Execute(VisaoDashboard.Vendas);
        await Task.Delay(300);

        Assert.True(viewModel.SemItensVendidosNoMes);
        Assert.Empty(viewModel.ProdutosMaisVendidosMes);
    }

    [StaFact]
    public void Seletor_ComecaEmMes_ETrocarParaTotalRecarregaOsBlocos()
    {
        var (tela, viewModel) = Criar(Vendas());
        viewModel.TrocarVisaoCommand.Execute(VisaoDashboard.Vendas);

        Assert.True(viewModel.PeriodoEhMes);
        Assert.Equal("Itens Vendidos por Categoria (mês)", viewModel.TituloItensPorCategoria);

        viewModel.TrocarPeriodoProdutosCommand.Execute(PeriodoDashboard.Total);

        Assert.True(viewModel.PeriodoEhTotal);
        Assert.False(viewModel.PeriodoEhMes);
        Assert.Equal("Itens Vendidos por Categoria (total)", viewModel.TituloItensPorCategoria);
        Assert.Equal("5 Mais Vendidos (total)", viewModel.TituloMaisVendidos);

        Assert.NotNull(tela);
    }

    // Os testes de Seletor_* não esperam com Task.Delay de propósito: os serviços falsos
    // completam na hora, então Execute já termina tudo na própria thread. Esperar moveria a
    // continuação para o pool, e dali o ViewModel pede ao dispatcher da janela de teste — que
    // não bombeia — e trava. No programa a thread da interface é a do dispatcher.

    /// <summary>Trocar o período recarrega só os blocos de produtos: as "Maiores Vendas"
    /// seguem sendo do mês e não precisam de nova consulta.</summary>
    [StaFact]
    public void Seletor_TrocarDePeriodo_NaoRecarregaAsMaioresVendas()
    {
        var servico = new Mock<IDashboardService>();
        servico.Setup(s => s.ObterVisaoVendasAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DashboardVendasDto());
        servico.Setup(s => s.ObterProdutosVendidosAsync(It.IsAny<PeriodoDashboard>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PeriodoDashboard p, CancellationToken _) => new DashboardProdutosVendidosDto { Periodo = p });
        var (tela, viewModel) = Criar(servico);

        viewModel.TrocarVisaoCommand.Execute(VisaoDashboard.Vendas);
        viewModel.TrocarPeriodoProdutosCommand.Execute(PeriodoDashboard.Total);

        servico.Verify(s => s.ObterVisaoVendasAsync(It.IsAny<CancellationToken>()), Times.Once);
        servico.Verify(s => s.ObterProdutosVendidosAsync(PeriodoDashboard.Total, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(tela);
    }

    /// <summary>Clicar no período que já está marcado não pode consultar de novo.</summary>
    [StaFact]
    public void Seletor_ClicarNoPeriodoJaMarcado_NaoConsultaDeNovo()
    {
        var servico = new Mock<IDashboardService>();
        servico.Setup(s => s.ObterVisaoVendasAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new DashboardVendasDto());
        servico.Setup(s => s.ObterProdutosVendidosAsync(It.IsAny<PeriodoDashboard>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PeriodoDashboard p, CancellationToken _) => new DashboardProdutosVendidosDto { Periodo = p });
        var (tela, viewModel) = Criar(servico);

        viewModel.TrocarVisaoCommand.Execute(VisaoDashboard.Vendas);
        viewModel.TrocarPeriodoProdutosCommand.Execute(PeriodoDashboard.Mes);

        servico.Verify(s => s.ObterProdutosVendidosAsync(It.IsAny<PeriodoDashboard>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(tela);
    }

    [StaFact]
    public void Tela_ConstroiComOsBlocosNovosNoXaml()
    {
        var (tela, _) = Criar(Vendas());

        // Os bindings só se ligam com a tela dentro de uma janela mostrada: solta, o
        // IsChecked dos botões ainda está "Unattached" e lê como falso.
        var janela = new Window { Content = tela, Width = 1200, Height = 800 };
        janela.Show();
        janela.UpdateLayout();

        // Percorre a árvore atrás dos títulos dos dois cards: se o XAML novo tivesse
        // StaticResource inexistente, a construção acima já teria estourado.
        var textos = new List<string>();
        void Visitar(DependencyObject no)
        {
            if (no is TextBlock tb) textos.Add(tb.Text);
            foreach (var filho in LogicalTreeHelper.GetChildren(no).OfType<DependencyObject>())
                Visitar(filho);
        }
        Visitar(tela);

        var mes = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(tela.FindName("BtnPeriodoMes"));
        var total = Assert.IsType<System.Windows.Controls.Primitives.ToggleButton>(tela.FindName("BtnPeriodoTotal"));
        Assert.Equal("Mês", mes.Content);
        Assert.Equal("Total", total.Content);
        // Começa em Mês: é o recorte que o Dashboard sempre mostrou.
        Assert.True(mes.IsChecked);
        Assert.False(total.IsChecked);
        Assert.Contains("Itens Vendidos por Categoria (mês)", textos);
        Assert.Contains("5 Mais Vendidos (mês)", textos);

        janela.Close();
    }
}
