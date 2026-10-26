using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using ImperialColors.UI.Helpers;
using ImperialColors.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ImperialColors.UI.Tests;

public class NotaFiscalFormCalculoTests
{
    public NotaFiscalFormCalculoTests()
    {
        WpfTestBootstrap.Inicializar();
        FormattingHelper.ConfigurarCulturaThread();
    }

    private static readonly string[] CamposEditaveis =
    {
        "TxtVProd", "TxtVFrete", "TxtVSeg", "TxtVBcIcms", "TxtVIcms", "TxtVBcIcmsSt", "TxtVIcmsSt",
        "TxtVIpi", "TxtVIpiDevolvido", "TxtVOutro", "TxtVDesc", "TxtVFunrural", "TxtVAproxImp",
        "TxtVFcp", "TxtVFcpSt", "TxtVFcpStRet"
    };

    private static NotaFiscalFormView Criar(NotaFiscalDto nota, Mock<INotaFiscalService>? fiscal = null)
    {
        // Usa o cálculo real; os repositórios não são acessados nestes testes de formulário.
        var calculadora = new NotaFiscalService(
            Mock.Of<INotaFiscalRepository>(), Mock.Of<IVendaRepository>(), Mock.Of<IClienteRepository>(),
            Mock.Of<IProdutoRepository>(), Mock.Of<ITributacaoProdutoRepository>(),
            Mock.Of<ITributacaoCategoriaRepository>(), Mock.Of<IRepository<NaturezaOperacao>>(),
            Mock.Of<IConfiguracaoFiscalService>(), Mock.Of<IFiscalApiClient>());
        fiscal ??= new Mock<INotaFiscalService>();
        fiscal.Setup(s => s.RecalcularTotais(It.IsAny<NotaFiscalDto>()))
            .Returns((NotaFiscalDto dto) => calculadora.RecalcularTotais(dto));

        var servicos = new ServiceCollection();
        servicos.AddSingleton(fiscal.Object);
        servicos.AddSingleton(Mock.Of<IClienteService>());
        servicos.AddSingleton(Mock.Of<IProdutoService>());
        servicos.AddSingleton(Mock.Of<INaturezaOperacaoService>());
        servicos.AddSingleton(Mock.Of<IConfiguracaoFiscalService>());
        servicos.AddSingleton(Mock.Of<IViaCepService>());
        var tela = new NotaFiscalFormView(servicos.BuildServiceProvider(), nota.Tipo, notaFiscalId: null);
        Campo("_nota").SetValue(tela, nota);
        Invocar(tela, "PreencherFormulario", nota);
        Campo("_carregando").SetValue(tela, false);
        return tela;
    }

    private static FieldInfo Campo(string nome) => typeof(NotaFiscalFormView)
        .GetField(nome, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void Invocar(NotaFiscalFormView tela, string nome, params object[] argumentos)
    {
        try
        {
            typeof(NotaFiscalFormView).GetMethod(nome, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(tela, argumentos);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    private static TextBox Caixa(NotaFiscalFormView tela, string nome) => (TextBox)tela.FindName(nome)!;
    private static CheckBox Automatico(NotaFiscalFormView tela) => (CheckBox)tela.FindName("ChkCalculoAutomatico")!;

    [StaFact]
    public void DesligarCalculoAutomatico_LiberaCamposEMantemTotalEContagemCalculados()
    {
        var tela = Criar(new NotaFiscalDto());
        try
        {
            Assert.True(Automatico(tela).IsChecked);
            Assert.All(CamposEditaveis, nome => Assert.True(Caixa(tela, nome).IsReadOnly));

            Automatico(tela).IsChecked = false;

            Assert.All(CamposEditaveis, nome => Assert.False(Caixa(tela, nome).IsReadOnly));
            Assert.True(Caixa(tela, "TxtVNf").IsReadOnly);
            Assert.True(Caixa(tela, "TxtNumeroItens").IsReadOnly);
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void CalculoManual_LeTodosOsValoresDigitadosSemSobrescreverNoSalvar()
    {
        var nota = new NotaFiscalDto { Itens = new() { new() { ValorTotal = 100m } }, NumeroItens = 1 };
        var tela = Criar(nota);
        try
        {
            Automatico(tela).IsChecked = false;
            var valores = new[]
            {
                "1.234,56", "10,25", "2,50", "100,40", "18,07", "151,20", "5,30", "3,20",
                "0,70", "4,10", "20,00", "0,12", "40,25", "0,15", "0,20", "0,10"
            };
            for (var i = 0; i < CamposEditaveis.Length; i++)
                Caixa(tela, CamposEditaveis[i]).Text = valores[i];

            Invocar(tela, "LerFormularioParaDto");

            Assert.False(nota.CalculoAutomatico);
            Assert.Equal(new[]
            {
                1234.56m, 10.25m, 2.50m, 100.40m, 18.07m, 151.20m, 5.30m, 3.20m,
                0.70m, 4.10m, 20m, 0.12m, 40.25m, 0.15m, 0.20m, 0.10m
            }, new[]
            {
                nota.VProd, nota.VFrete, nota.VSeg, nota.VBcIcms, nota.VIcms, nota.VBcIcmsSt,
                nota.VIcmsSt, nota.VIpi, nota.VIpiDevolvido, nota.VOutro, nota.VDesc, nota.VFunrural,
                nota.VAproxImp, nota.VFcp, nota.VFcpSt, nota.VFcpStRet
            });
            Assert.Equal(1239.91m, nota.VNf);
            Assert.Equal("1239,91", Caixa(tela, "TxtVNf").Text);
            Assert.Equal(1, nota.NumeroItens);
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void AlteracaoManual_AtualizaTotalDuranteDigitacao()
    {
        var tela = Criar(new NotaFiscalDto { CalculoAutomatico = false, VProd = 100m, VNf = 100m });
        try
        {
            Caixa(tela, "TxtVIcmsSt").Text = "15,00";
            Caixa(tela, "TxtVFrete").Text = "10,00";
            Caixa(tela, "TxtVSeg").Text = "2,00";
            Caixa(tela, "TxtVOutro").Text = "3,00";
            Caixa(tela, "TxtVIpi").Text = "5,00";
            Caixa(tela, "TxtVDesc").Text = "20,00";

            Assert.Equal("115,00", Caixa(tela, "TxtVNf").Text);
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void AlterarItensEmModoManual_PreservaDigitacaoEAjustaContagem()
    {
        var nota = new NotaFiscalDto { CalculoAutomatico = false, VProd = 100m, VNf = 100m };
        var tela = Criar(nota);
        try
        {
            Caixa(tela, "TxtVProd").Text = "777,55";
            Caixa(tela, "TxtVIcms").Text = "23,45";
            var itens = (ObservableCollection<ItemNotaFiscalDto>)Campo("_itens").GetValue(tela)!;
            itens.Add(new ItemNotaFiscalDto { ValorTotal = 50m });
            itens.Add(new ItemNotaFiscalDto { ValorTotal = 25m });

            Invocar(tela, "RecalcularTotaisUi");

            Assert.Equal("777,55", Caixa(tela, "TxtVProd").Text);
            Assert.Equal("23,45", Caixa(tela, "TxtVIcms").Text);
            Assert.Equal("2", Caixa(tela, "TxtNumeroItens").Text);
            Assert.False(nota.CalculoAutomatico);
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void ReligarCalculoAutomatico_RecalculaImpostosDosItens()
    {
        var nota = new NotaFiscalDto
        {
            CalculoAutomatico = false, VProd = 999m, VIcms = 888m, VIpi = 777m,
            Itens = new() { new() { ValorTotal = 100m, BaseIcms = 100m, ValorIcms = 18m, ValorIpi = 5m } }
        };
        var tela = Criar(nota);
        try
        {
            Automatico(tela).IsChecked = true;

            Assert.True(nota.CalculoAutomatico);
            Assert.Equal("100,00", Caixa(tela, "TxtVProd").Text);
            Assert.Equal("18,00", Caixa(tela, "TxtVIcms").Text);
            Assert.Equal("5,00", Caixa(tela, "TxtVIpi").Text);
            Assert.Equal("105,00", Caixa(tela, "TxtVNf").Text);
            Assert.All(CamposEditaveis, nome => Assert.True(Caixa(tela, nome).IsReadOnly));
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void ReligarCalculoAutomatico_PreservaFreteSeguroDespesasEDescontoDigitados()
    {
        var nota = new NotaFiscalDto
        {
            CalculoAutomatico = false,
            Itens = new() { new() { ValorTotal = 100m } }
        };
        var tela = Criar(nota);
        try
        {
            Caixa(tela, "TxtVFrete").Text = "10,00";
            Caixa(tela, "TxtVSeg").Text = "2,00";
            Caixa(tela, "TxtVOutro").Text = "3,00";
            Caixa(tela, "TxtVDesc").Text = "20,00";

            Automatico(tela).IsChecked = true;

            Assert.Equal(10m, nota.VFrete);
            Assert.Equal(2m, nota.VSeg);
            Assert.Equal(3m, nota.VOutro);
            Assert.Equal(20m, nota.VDesc);
            Assert.Equal("95,00", Caixa(tela, "TxtVNf").Text);
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void NotaAutorizada_MantemCalculosBloqueadosMesmoNoModoManual()
    {
        var tela = Criar(new NotaFiscalDto { Status = StatusNotaFiscal.Autorizada, CalculoAutomatico = false });
        try
        {
            Assert.All(CamposEditaveis, nome => Assert.True(Caixa(tela, nome).IsReadOnly));
            Assert.False(Automatico(tela).IsEnabled);
            Assert.False(((Button)tela.FindName("BtnSalvarRascunho")!).IsEnabled);
        }
        finally { tela.Close(); }
    }

    [StaTheory]
    [InlineData("abc")]
    [InlineData("-1,00")]
    public void ValorManualInvalido_ImpedeLeituraParaSalvar(string texto)
    {
        var tela = Criar(new NotaFiscalDto { CalculoAutomatico = false });
        try
        {
            Caixa(tela, "TxtVFrete").Text = texto;

            Assert.Throws<DomainException>(() => Invocar(tela, "LerFormularioParaDto"));
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void ValorManualEmBranco_EhSalvoComoZero()
    {
        var nota = new NotaFiscalDto { CalculoAutomatico = false, VFrete = 10m };
        var tela = Criar(nota);
        try
        {
            Caixa(tela, "TxtVFrete").Text = string.Empty;

            Invocar(tela, "LerFormularioParaDto");

            Assert.Equal(0m, nota.VFrete);
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void DescontoMaiorQueTotal_ImpedeSalvarTotalNegativo()
    {
        var tela = Criar(new NotaFiscalDto { CalculoAutomatico = false, VProd = 100m, VNf = 100m });
        try
        {
            Caixa(tela, "TxtVDesc").Text = "101,00";

            var erro = Assert.Throws<DomainException>(() => Invocar(tela, "LerFormularioParaDto"));

            Assert.Contains("total da nota negativo", erro.Message);
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void SalvarComValorManualInvalido_MostraMensagemSemChamarServico()
    {
        var fiscal = new Mock<INotaFiscalService>();
        var tela = Criar(new NotaFiscalDto { CalculoAutomatico = false }, fiscal);
        try
        {
            Caixa(tela, "TxtVFrete").Text = "inválido";
            var salvar = (Button)tela.FindName("BtnSalvarRascunho")!;

            Invocar(tela, "BtnSalvarRascunho_Click", salvar, new RoutedEventArgs());

            Assert.Contains("Valor do Frete", ((TextBlock)tela.FindName("TxtMensagemStatus")!).Text);
            Assert.True(salvar.IsEnabled);
            fiscal.Verify(s => s.CriarRascunhoAsync(It.IsAny<NotaFiscalDto>(), It.IsAny<CancellationToken>()), Times.Never);
            fiscal.Verify(s => s.AtualizarRascunhoAsync(It.IsAny<NotaFiscalDto>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void RascunhoManualReaberto_ExibeModoETotaisSalvosSemRecalcularDosItens()
    {
        var nota = new NotaFiscalDto
        {
            Id = 42, CalculoAutomatico = false, VProd = 250m, VFrete = 5.50m,
            VDesc = 10m, VIcms = 17.37m, VNf = 245.50m, NumeroItens = 1,
            Itens = new() { new() { ValorTotal = 100m, ValorIcms = 18m } }
        };
        var tela = Criar(nota);
        try
        {
            Assert.False(Automatico(tela).IsChecked);
            Assert.False(Caixa(tela, "TxtVIcms").IsReadOnly);
            Assert.Equal("250,00", Caixa(tela, "TxtVProd").Text);
            Assert.Equal("17,37", Caixa(tela, "TxtVIcms").Text);
            Assert.Equal("245,50", Caixa(tela, "TxtVNf").Text);
            Assert.Equal("1", Caixa(tela, "TxtNumeroItens").Text);
        }
        finally { tela.Close(); }
    }

    [StaFact]
    public void CasasDecimaisExtras_TotalExibidoArredondaCadaCampoIgualAoSalvar()
    {
        var nota = new NotaFiscalDto { CalculoAutomatico = false };
        var tela = Criar(nota);
        try
        {
            Caixa(tela, "TxtVProd").Text = "1,005";
            Caixa(tela, "TxtVFrete").Text = "0,005";
            Caixa(tela, "TxtVSeg").Text = "0,005";

            Assert.Equal("1,03", Caixa(tela, "TxtVNf").Text);

            Invocar(tela, "LerFormularioParaDto");

            Assert.Equal(1.01m, nota.VProd);
            Assert.Equal(0.01m, nota.VFrete);
            Assert.Equal(0.01m, nota.VSeg);
            Assert.Equal(1.03m, nota.VNf);
            Assert.Equal("1,03", Caixa(tela, "TxtVNf").Text);
        }
        finally { tela.Close(); }
    }
}
