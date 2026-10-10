using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.UI.Converters;
using ImperialColors.UI.ViewModels;
using ImperialColors.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// Lista de estoque com a quantidade e a unidade de medida em colunas separadas. Antes havia
/// uma coluna só ("3,6 Litros"), que não ordenava como número e misturava duas informações
/// que o lojista confere de formas diferentes.
/// </summary>
public class EstoqueColunasQuantidadeUnidadeTests
{
    public EstoqueColunasQuantidadeUnidadeTests() => WpfTestBootstrap.Inicializar();

    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    // ---------- Conversores ----------

    [Theory]
    [InlineData(10, "10")]
    [InlineData(3.6, "3,6")]
    [InlineData(0, "0")]
    [InlineData(1234.5, "1.234,5")]
    public void Quantidade_SaiNoFormatoBrasileiroSemZerosASobrar(double valor, string esperado)
    {
        // Cultura en-US de propósito: é a padrão de um elemento WPF sem Language definido, e
        // o conversor não pode depender dela — "3.6" no lugar de "3,6" seria o erro.
        var texto = new QuantidadeConverter().Convert((decimal)valor, typeof(string), null!, EnUs);

        Assert.Equal(esperado, texto);
    }

    [Fact]
    public void Quantidade_QuantidadeFracionadaNaoPerdeCasas()
    {
        var texto = new QuantidadeConverter().Convert(1.25m, typeof(string), null!, EnUs);

        // Com "N1", 1,25 aparecia como 1,3 — e o lojista conferia o estoque errado.
        Assert.Equal("1,25", texto);
    }

    [Theory]
    [InlineData("UN", "UN")]
    [InlineData("gl", "GL")]
    [InlineData(" lt ", "LT")]
    [InlineData("", "UN")]
    [InlineData(null, "UN")]
    public void Unidade_MostraASiglaECaiEmUnQuandoVazia(string? entrada, string esperado)
    {
        var texto = new UnidadeMedidaConverter().Convert(entrada!, typeof(string), null!, EnUs);

        Assert.Equal(esperado, texto);
    }

    // ---------- Tela ----------

    private static (EstoqueView Tela, ProdutoViewModel ViewModel) Criar()
    {
        var viewModel = new ProdutoViewModel(
            Mock.Of<IProdutoService>(),
            Mock.Of<IServiceScopeFactory>());
        return (new EstoqueView(viewModel), viewModel);
    }

    private static DataGrid LocalizarGrade(DependencyObject raiz)
    {
        if (raiz is DataGrid grade)
            return grade;

        foreach (var filho in LogicalTreeHelper.GetChildren(raiz).OfType<DependencyObject>())
        {
            var achada = LocalizarGradeOuNulo(filho);
            if (achada is not null)
                return achada;
        }

        throw new InvalidOperationException("Nenhum DataGrid na tela de estoque.");
    }

    private static DataGrid? LocalizarGradeOuNulo(DependencyObject raiz)
    {
        if (raiz is DataGrid grade)
            return grade;

        foreach (var filho in LogicalTreeHelper.GetChildren(raiz).OfType<DependencyObject>())
        {
            var achada = LocalizarGradeOuNulo(filho);
            if (achada is not null)
                return achada;
        }

        return null;
    }

    [StaFact]
    public void Lista_TemQuantidadeEUnidadeSeparadasEMaisNenhumaColunaEstoque()
    {
        var (tela, _) = Criar();

        var cabecalhos = LocalizarGrade(tela).Columns.Select(c => c.Header?.ToString()).ToList();

        Assert.Contains("Quantidade", cabecalhos);
        Assert.Contains("Unidade", cabecalhos);
        Assert.DoesNotContain("Estoque", cabecalhos);

        // Quantidade vem antes da unidade, como se lê "10 UN".
        Assert.True(cabecalhos.IndexOf("Quantidade") < cabecalhos.IndexOf("Unidade"));
    }

    /// <summary>
    /// A quantidade ordena como número. Com o texto formatado, "10" viria antes de "9" — e o
    /// "menor estoque primeiro" mostraria o produto errado no topo.
    /// </summary>
    [StaFact]
    public void Lista_QuantidadeOrdenaPeloValorNaoPeloTexto()
    {
        var (tela, _) = Criar();

        var colunas = LocalizarGrade(tela).Columns.OfType<DataGridTextColumn>().ToList();

        Assert.Equal("QuantidadeEstoque", colunas.Single(c => c.Header?.ToString() == "Quantidade").SortMemberPath);
        Assert.Equal("Unidade", colunas.Single(c => c.Header?.ToString() == "Unidade").SortMemberPath);
    }

    /// <summary>Renderiza a tela com produtos e confere o texto das duas colunas de verdade —
    /// binding com converter que não resolve só aparece em tempo de execução.</summary>
    [StaFact]
    public void Lista_MostraQuantidadeEUnidadeEmCelulasDistintas()
    {
        var (tela, viewModel) = Criar();
        viewModel.Produtos = new System.Collections.ObjectModel.ObservableCollection<ProdutoDto>
        {
            new() { Id = 1, CodigoInterno = "TAB-001", Nome = "Tinta Branca", QuantidadeEstoque = 3.6m, EstoqueMinimo = 1m, Unidade = "LT", PrecoVenda = 10m },
            new() { Id = 2, CodigoInterno = "PIN-002", Nome = "Pincel", QuantidadeEstoque = 12m, EstoqueMinimo = 1m, Unidade = "UN", PrecoVenda = 5m }
        };

        var janela = new Window { Content = tela, Width = 1400, Height = 700 };
        janela.Show();
        janela.UpdateLayout();

        var grade = LocalizarGrade(tela);
        var colunaQuantidade = grade.Columns.Single(c => c.Header?.ToString() == "Quantidade");
        var colunaUnidade = grade.Columns.Single(c => c.Header?.ToString() == "Unidade");

        string TextoDaCelula(int linha, DataGridColumn coluna)
        {
            grade.UpdateLayout();
            grade.ScrollIntoView(grade.Items[linha], coluna);
            grade.UpdateLayout();
            var conteudo = coluna.GetCellContent(grade.Items[linha]);
            return Assert.IsType<TextBlock>(conteudo).Text;
        }

        Assert.Equal("3,6", TextoDaCelula(0, colunaQuantidade));
        Assert.Equal("LT", TextoDaCelula(0, colunaUnidade));
        Assert.Equal("12", TextoDaCelula(1, colunaQuantidade));
        Assert.Equal("UN", TextoDaCelula(1, colunaUnidade));

        janela.Close();
    }
}
