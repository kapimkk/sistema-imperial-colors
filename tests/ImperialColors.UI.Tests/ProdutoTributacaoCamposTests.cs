using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Enums;
using ImperialColors.UI.Helpers;
using ImperialColors.UI.Views;
using Moq;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// Cada dado fiscal é exercitado sozinho: um NCM já preenchido não pode esconder a perda
/// de outro campo na detecção de tributação preenchida, na montagem ou na reabertura.
/// </summary>
public class ProdutoTributacaoCamposTests
{
    public ProdutoTributacaoCamposTests()
    {
        WpfTestBootstrap.Inicializar();
        FormattingHelper.ConfigurarCulturaThread();
    }

    private static readonly (string Propriedade, string Campo, string Texto, object Valor)[] Dados =
    {
        (nameof(TributacaoProdutoDto.Ncm), "TxtNcm", "32091010", "32091010"),
        (nameof(TributacaoProdutoDto.Cest), "TxtCest", "2400100", "2400100"),
        (nameof(TributacaoProdutoDto.Origem), "CmbOrigem", "0", OrigemMercadoria.Nacional),
        (nameof(TributacaoProdutoDto.CstIcms), "TxtCstIcms", "60", "60"),
        (nameof(TributacaoProdutoDto.CsosnIcms), "TxtCsosnIcms", "500", "500"),
        (nameof(TributacaoProdutoDto.AliquotaIcms), "TxtAliquotaIcms", "18,12", 18.12m),
        (nameof(TributacaoProdutoDto.AliquotaIcmsSt), "TxtAliquotaIcmsSt", "19,13", 19.13m),
        (nameof(TributacaoProdutoDto.Mva), "TxtMva", "42,14", 42.14m),
        (nameof(TributacaoProdutoDto.ReducaoBaseCalculo), "TxtReducaoBaseCalculo", "33,15", 33.15m),
        (nameof(TributacaoProdutoDto.AliquotaIcmsStRetido), "TxtAliquotaIcmsStRetido", "12,16", 12.16m),
        (nameof(TributacaoProdutoDto.CstPis), "TxtCstPis", "01", "01"),
        (nameof(TributacaoProdutoDto.AliquotaPis), "TxtAliquotaPis", "1,65", 1.65m),
        (nameof(TributacaoProdutoDto.CstCofins), "TxtCstCofins", "02", "02"),
        (nameof(TributacaoProdutoDto.AliquotaCofins), "TxtAliquotaCofins", "7,66", 7.66m),
        (nameof(TributacaoProdutoDto.CstIpi), "TxtCstIpi", "50", "50"),
        (nameof(TributacaoProdutoDto.CodigoEnquadramentoIpi), "TxtCodigoEnquadramentoIpi", "999", "999"),
        (nameof(TributacaoProdutoDto.AliquotaIpi), "TxtAliquotaIpi", "5,17", 5.17m),
        (nameof(TributacaoProdutoDto.ValorIpiFixo), "TxtValorIpiFixo", "2,18", 2.18m),
        (nameof(TributacaoProdutoDto.ExTipi), "TxtExTipi", "001", "001"),
        (nameof(TributacaoProdutoDto.UnidadeTributavel), "TxtUnidadeTributavel", "LT", "LT"),
        (nameof(TributacaoProdutoDto.FatorConversao), "TxtFatorConversao", "1,2345", 1.2345m),
        (nameof(TributacaoProdutoDto.GtinTributavel), "TxtGtinTributavel", "7894900011517", "7894900011517"),
        (nameof(TributacaoProdutoDto.CfopDentroEstado), "TxtCfopDentroEstado", "5102", "5102"),
        (nameof(TributacaoProdutoDto.CfopForaEstado), "TxtCfopForaEstado", "6102", "6102"),
        (nameof(TributacaoProdutoDto.CstIbsCbs), "TxtCstIbsCbs", "200", "200"),
        (nameof(TributacaoProdutoDto.CClassTrib), "TxtCClassTrib", "200001", "200001"),
        (nameof(TributacaoProdutoDto.CstIS), "TxtCstIS", "001", "001"),
        (nameof(TributacaoProdutoDto.CClassTribIS), "TxtCClassTribIS", "001001", "001001"),
        (nameof(TributacaoProdutoDto.AliquotaIS), "TxtAliquotaIS", "3,19", 3.19m),
        (nameof(TributacaoProdutoDto.AliquotaIbsMunicipioDiferimento), "TxtAliquotaIbsMunicipioDiferimento", "25,20", 25.20m),
        (nameof(TributacaoProdutoDto.AliquotaIbsMunicipioReducao), "TxtAliquotaIbsMunicipioReducao", "10,21", 10.21m)
    };

    public static IEnumerable<object[]> Campos()
    {
        foreach (var dado in Dados)
            yield return new[] { dado.Propriedade, dado.Campo, dado.Texto, dado.Valor };

        // A origem nacional é zero e precisa continuar diferente de ausência de origem.
        foreach (var origem in Enum.GetValues<OrigemMercadoria>().Where(o => o != OrigemMercadoria.Nacional))
            yield return new object[] { nameof(TributacaoProdutoDto.Origem), "CmbOrigem", ((int)origem).ToString(CultureInfo.InvariantCulture), origem };

        // Zero explícito não pode virar null, mesmo em campos cuja faixa será validada ao salvar.
        foreach (var dado in Dados.Where(d => d.Valor is decimal))
            yield return new object[] { dado.Propriedade, dado.Campo, "0", 0m };
    }

    public static IEnumerable<object[]> CamposParaReabrir()
        => Campos().Select(c => new[] { c[0], c[1], c[3] });

    private static ProdutoFormView Criar(Mock<IProdutoService>? produto = null) => new(
        (produto ?? new Mock<IProdutoService>()).Object,
        Mock.Of<ICategoriaService>(), Mock.Of<IMarcaService>(), Mock.Of<IFornecedorService>(),
        Mock.Of<IConfiguracaoFiscalService>(), Mock.Of<INcmService>());

    private static object? Invocar(ProdutoFormView tela, string metodo, params object[] argumentos)
    {
        try
        {
            return typeof(ProdutoFormView).GetMethod(metodo, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(tela, argumentos);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static TributacaoProdutoDto? Montar(ProdutoFormView tela)
        => (TributacaoProdutoDto?)Invocar(tela, "MontarDtoTributacaoOuNulo");

    private static void Definir(ProdutoFormView tela, string campo, string texto)
    {
        if (campo == "CmbOrigem")
        {
            var combo = (ComboBox)tela.FindName(campo)!;
            combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().Single(i => i.Tag?.ToString() == texto);
        }
        else if (campo == "TxtNcm")
            Invocar(tela, "DefinirNcmSemAutocomplete", texto);
        else
            ((TextBox)tela.FindName(campo)!).Text = texto;
    }

    private static void ConferirValor(ProdutoFormView tela, string campo, object esperado)
    {
        if (esperado is OrigemMercadoria origem)
        {
            var item = Assert.IsType<ComboBoxItem>(((ComboBox)tela.FindName(campo)!).SelectedItem);
            Assert.Equal(((int)origem).ToString(CultureInfo.InvariantCulture), item.Tag?.ToString());
        }
        else
        {
            var texto = ((TextBox)tela.FindName(campo)!).Text;
            if (esperado is decimal numero)
            {
                Assert.True(FormattingHelper.TryParseMoeda(texto, out var lido), $"{campo} não foi preenchido com número: '{texto}'.");
                Assert.Equal(numero, lido);
            }
            else
                Assert.Equal((string)esperado, texto);
        }
    }

    private static void ConferirSomenteCampo(TributacaoProdutoDto dto, string propriedade, object esperado)
    {
        foreach (var dado in Dados)
        {
            var valor = typeof(TributacaoProdutoDto).GetProperty(dado.Propriedade)!.GetValue(dto);
            if (dado.Propriedade == propriedade)
                Assert.Equal(esperado, valor);
            else
                Assert.Null(valor);
        }
    }

    [StaTheory]
    [MemberData(nameof(Campos))]
    public void CampoFiscalIsolado_MontaDtoSemPerderValorOuAdicionarOutrosCampos(
        string propriedade, string campo, string texto, object esperado)
    {
        var tela = Criar();
        try
        {
            Assert.Null(Montar(tela));
            Definir(tela, campo, texto);

            var dto = Assert.IsType<TributacaoProdutoDto>(Montar(tela));

            ConferirSomenteCampo(dto, propriedade, esperado);
            Assert.True(dto.Preenchida);
        }
        finally { tela.Close(); }
    }

    [StaTheory]
    [MemberData(nameof(CamposParaReabrir))]
    public async Task CampoFiscalIsoladoSalvo_ReabreExatamenteComoFoiGravado(
        string propriedade, string campo, object esperado)
    {
        var dto = new TributacaoProdutoDto { ProdutoId = 42 };
        typeof(TributacaoProdutoDto).GetProperty(propriedade)!.SetValue(dto, esperado);
        var produto = new Mock<IProdutoService>();
        produto.Setup(s => s.ObterTributacaoAsync(42)).ReturnsAsync(dto);
        var tela = Criar(produto);
        try
        {
            await (Task)Invocar(tela, "CarregarTributacaoAsync", 42)!;

            ConferirValor(tela, campo, esperado);
            Assert.True(((Expander)tela.FindName("ExpTributacao")!).IsExpanded);
            var remontado = Assert.IsType<TributacaoProdutoDto>(Montar(tela));
            ConferirSomenteCampo(remontado, propriedade, esperado);
            produto.Verify(s => s.ObterTributacaoAsync(42), Times.Once);
        }
        finally { tela.Close(); }
    }

    [StaTheory]
    [MemberData(nameof(Campos))]
    public void LimparCampoFiscal_PreservaNulosEDistingueAusenciaDeZero(
        string propriedade, string campo, string texto, object esperado)
    {
        var tela = Criar();
        try
        {
            Definir(tela, campo, texto);
            Assert.Equal(esperado, typeof(TributacaoProdutoDto).GetProperty(propriedade)!.GetValue(Montar(tela)));
            // Mantém outro campo para testar o null do campo limpo dentro de um DTO existente.
            var apoio = propriedade == nameof(TributacaoProdutoDto.Ncm)
                ? Dados.Single(d => d.Propriedade == nameof(TributacaoProdutoDto.Cest))
                : Dados.Single(d => d.Propriedade == nameof(TributacaoProdutoDto.Ncm));
            Definir(tela, apoio.Campo, apoio.Texto);
            if (campo == "CmbOrigem")
                ((ComboBox)tela.FindName(campo)!).SelectedItem = null;
            else
                Definir(tela, campo, "   ");

            var dto = Assert.IsType<TributacaoProdutoDto>(Montar(tela));

            Assert.Null(typeof(TributacaoProdutoDto).GetProperty(propriedade)!.GetValue(dto));
            Assert.Equal(apoio.Valor, typeof(TributacaoProdutoDto).GetProperty(apoio.Propriedade)!.GetValue(dto));
            Invocar(tela, "LimparCamposTributacao");
            Assert.Null(Montar(tela));
        }
        finally { tela.Close(); }
    }

    [Fact]
    public void Cobertura_IncluiTodosOsCamposFiscaisEditaveisDoDto()
    {
        var propriedades = typeof(TributacaoProdutoDto).GetProperties()
            .Where(p => p.CanWrite && p.Name != nameof(TributacaoProdutoDto.ProdutoId))
            .Select(p => p.Name).OrderBy(n => n);

        Assert.Equal(propriedades, Dados.Select(d => d.Propriedade).OrderBy(n => n));
    }

    [StaFact]
    public async Task LimparTributacaoExistente_ProduzDtoVazioParaSalvarENovoProdutoResetaEsseEstado()
    {
        var salvo = new TributacaoProdutoDto { ProdutoId = 42 };
        foreach (var dado in Dados)
            typeof(TributacaoProdutoDto).GetProperty(dado.Propriedade)!.SetValue(salvo, dado.Valor);
        var produto = new Mock<IProdutoService>();
        produto.Setup(s => s.ObterTributacaoAsync(42)).ReturnsAsync(salvo);
        var tela = Criar(produto);
        try
        {
            Assert.Null(Montar(tela));
            await (Task)Invocar(tela, "CarregarTributacaoAsync", 42)!;
            foreach (var dado in Dados)
                ConferirValor(tela, dado.Campo, dado.Valor);

            Invocar(tela, "LimparCamposTributacao");
            var vazioParaSalvar = Assert.IsType<TributacaoProdutoDto>(Montar(tela));

            Assert.False(vazioParaSalvar.Preenchida);
            foreach (var dado in Dados)
                Assert.Null(typeof(TributacaoProdutoDto).GetProperty(dado.Propriedade)!.GetValue(vazioParaSalvar));

            tela.InicializarNovo();
            Assert.Null(Montar(tela));
        }
        finally { tela.Close(); }
    }
}
