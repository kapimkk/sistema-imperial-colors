using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.UI.Views;
using Moq;
using System.Windows.Controls;
using System.Windows.Threading;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// Ao abrir um produto para edição, categoria e marca têm que aparecer como estão
/// cadastradas. O bug real: o produto da marca "Paraná Color" abria como "ATLAS" (a primeira
/// da lista), e o mesmo com a categoria. Nada era gravado errado — a tela só mentia —, mas
/// salvar sem reparar trocaria a marca do produto.
///
/// Causa: dois carregamentos das listas disputavam o resultado. Um vinha de
/// <c>InicializarEdicao</c> (com a categoria e a marca do produto) e outro do evento Loaded
/// (sem nenhuma, que cai no primeiro item). O último a terminar vencia — e era o do Loaded.
/// </summary>
public class ProdutoFormSelecaoCombosTests
{
    public ProdutoFormSelecaoCombosTests() => WpfTestBootstrap.Inicializar();

    private static ProdutoFormView CriarForm()
    {
        var categorias = new Mock<ICategoriaService>();
        categorias.Setup(c => c.ObterTodosAsync()).ReturnsAsync(new List<CategoriaDto>
        {
            new() { Id = 1, Nome = "ACESSORIOS" },
            new() { Id = 2, Nome = "TINTA" }
        });

        var marcas = new Mock<IMarcaService>();
        marcas.Setup(m => m.ObterTodosAsync()).ReturnsAsync(new List<MarcaDto>
        {
            new() { Id = 1, Nome = "ATLAS" },
            new() { Id = 2, Nome = "PARANA COLOR 2 BD" }
        });

        var fornecedores = new Mock<IFornecedorService>();
        fornecedores.Setup(f => f.ObterTodosAsync()).ReturnsAsync(new List<FornecedorDto>
        {
            new() { Id = 1, Nome = "Forn A" },
            new() { Id = 2, Nome = "Forn B" }
        });

        return new ProdutoFormView(
            Mock.Of<IProdutoService>(),
            categorias.Object,
            marcas.Object,
            fornecedores.Object,
            Mock.Of<IConfiguracaoFiscalService>(),
            Mock.Of<INcmService>());
    }

    private static ProdutoDto Produto() => new()
    {
        Id = 10,
        CodigoInterno = "301010006",
        Nome = "ECONOMICO AZUL CEU",
        PrecoVenda = 129.90m,
        Unidade = "UN",
        CategoriaId = 2,
        MarcaId = 2,
        FornecedorId = 2
    };

    /// <summary>Deixa o Loaded e o que ele dispara rodarem, como a janela faria ao abrir.</summary>
    private static void BombearDispatcher()
    {
        for (var i = 0; i < 5; i++)
        {
            var quadro = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => quadro.Continue = false));
            Dispatcher.PushFrame(quadro);
        }
    }

    [StaFact]
    public void Edicao_MantemMarcaCategoriaEFornecedorCadastrados()
    {
        var form = CriarForm();
        form.InicializarEdicao(Produto());

        form.Show();
        BombearDispatcher();

        var marca = Assert.IsType<MarcaDto>(((ComboBox)form.FindName("CmbMarca")).SelectedItem);
        var categoria = Assert.IsType<CategoriaDto>(((ComboBox)form.FindName("CmbCategoria")).SelectedItem);
        var fornecedor = Assert.IsType<FornecedorDto>(((ComboBox)form.FindName("CmbFornecedor")).SelectedItem);

        Assert.Equal("PARANA COLOR 2 BD", marca.Nome);
        Assert.Equal("TINTA", categoria.Nome);
        Assert.Equal(2, fornecedor.Id);

        form.Close();
    }

    /// <summary>Produto novo continua abrindo no primeiro item — é o comportamento que sempre
    /// existiu, e a correção da edição não pode mudá-lo.</summary>
    [StaFact]
    public void Novo_ContinuaComecandoNoPrimeiroItemDaLista()
    {
        var form = CriarForm();
        form.InicializarNovo();

        form.Show();
        BombearDispatcher();

        Assert.Equal("ATLAS", ((MarcaDto)((ComboBox)form.FindName("CmbMarca")).SelectedItem).Nome);
        Assert.Equal("ACESSORIOS", ((CategoriaDto)((ComboBox)form.FindName("CmbCategoria")).SelectedItem).Nome);

        form.Close();
    }

    /// <summary>
    /// Formulário reaproveitado: abriu um produto, fechou, abriu outro. A seleção do segundo
    /// não pode herdar a do primeiro.
    /// </summary>
    [StaFact]
    public void EdicaoDepoisDeNovo_NaoHerdaSelecaoAnterior()
    {
        var form = CriarForm();
        form.InicializarEdicao(Produto());
        form.InicializarNovo();

        form.Show();
        BombearDispatcher();

        Assert.Equal("ATLAS", ((MarcaDto)((ComboBox)form.FindName("CmbMarca")).SelectedItem).Nome);

        form.Close();
    }
}
