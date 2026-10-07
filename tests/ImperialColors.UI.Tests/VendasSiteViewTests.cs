using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.ReadModels;
using ImperialColors.UI.ViewModels;
using ImperialColors.UI.Views;
using Moq;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// A tela "Vendas Site" montada de verdade (XAML + tema + ViewModel): o que o operador vê em cada
/// estado, o botão "Sincronizar com o Site" bloqueado enquanto roda, e a disposição em janelas de
/// larguras diferentes. A lógica está em <c>VendasSiteViewModelTests</c>; aqui se confere que a
/// tela está LIGADA a ela.
/// </summary>
public class VendasSiteViewTests
{
    public VendasSiteViewTests() => WpfTestBootstrap.Inicializar();

    private sealed class Montagem
    {
        public Mock<IVendaSiteService> Vendas { get; } = new();
        public Mock<ISincronizacaoSiteService> Sincronizacao { get; } = new();
        public VendasSiteViewModel Vm { get; }
        public VendasSiteView Tela { get; }

        public Montagem(ResultadoVendasSiteDto? pagina = null)
        {
            Vendas
                .Setup(s => s.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(pagina ?? Pagina(0));
            Sincronizacao.SetupGet(s => s.EmExecucao).Returns(false);
            Sincronizacao
                .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResultadoSincronizacaoSite
                {
                    Status = StatusSincronizacaoSite.Concluida,
                    CodigoSaida = 0,
                    Mensagem = "Sincronização concluída com sucesso.",
                    ProcessoExecutado = true
                });
            Vm = new VendasSiteViewModel(Vendas.Object, Sincronizacao.Object);
            Tela = new VendasSiteView(Vm);
        }

        /// <summary>
        /// Procura um elemento da tela. Antes, deixa o WPF terminar o que adiou: as ligações de dados
        /// de um controle que ainda não está numa janela só são aplicadas quando a fila do
        /// Dispatcher (ou uma passada de layout) as processa — sem isto o teste leria o valor
        /// padrão do XAML em vez do que o ViewModel mandou.
        /// </summary>
        public T Achar<T>(string nome) where T : class
        {
            ProcessarFilaDeMensagens();
            return Tela.FindName(nome) as T ?? throw new InvalidOperationException($"Elemento '{nome}' não encontrado na tela.");
        }

        public Visibility Visibilidade(string nome) => Achar<UIElement>(nome).Visibility;

        public void Medir(double largura, double altura = 720)
        {
            Tela.Measure(new Size(largura, altura));
            Tela.Arrange(new Rect(0, 0, largura, altura));
            Tela.UpdateLayout();
        }
    }

    private static VendaSiteDto Venda(string pedido, string numero, string status = "Finalizada") => new()
    {
        OperacaoId = Guid.NewGuid(),
        PedidoSite = pedido,
        NumeroVenda = numero,
        VendaExiste = true,
        Cliente = "Cliente Teste Pix",
        Total = 365.47m,
        Pagamento = "Cartão de Crédito - 3x",
        Parcelas = 3,
        StatusDescricao = status,
        DataVenda = new DateTime(2026, 10, 6, 16, 45, 0),
        SincronizadoEm = new DateTime(2026, 10, 6, 16, 45, 56)
    };

    private static ResultadoVendasSiteDto Pagina(int total, params VendaSiteDto[] itens) => new()
    {
        Pagina = new PaginacaoResultadoDto<VendaSiteDto>
        {
            Itens = itens, PaginaAtual = 1, ItensPorPagina = 50, TotalItens = total
        }
    };

    /// <summary>Deixa o Dispatcher processar o que está pendente (o WPF reavalia os botões de um
    /// comando de forma adiada, em prioridade baixa).</summary>
    private static void ProcessarFilaDeMensagens()
    {
        var quadro = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => quadro.Continue = false));
        Dispatcher.PushFrame(quadro);
    }

    private static Color CorDoFundo(Border borda) => ((SolidColorBrush)borda.Background).Color;

    // ---------------------------------------------------------------------------------------
    // Estrutura
    // ---------------------------------------------------------------------------------------

    [WpfFact]
    public void Grade_TemAsColunasPedidas_NaOrdemCerta_ESomenteLeitura()
    {
        var m = new Montagem();
        var grade = m.Achar<DataGrid>("GridVendasSite");

        Assert.Equal(
            ["Pedido Site", "Nº Venda", "Cliente", "Data", "Total", "Pagamento", "Parcelas", "Status", "Sincronizado em"],
            grade.Columns.Select(c => c.Header?.ToString()));
        Assert.True(grade.IsReadOnly);
        Assert.False(grade.AutoGenerateColumns);
    }

    [WpfFact]
    public void Colunas_OrdenamPeloValorDeVerdade_NaoPeloTextoFormatado()
    {
        var m = new Montagem();
        var grade = m.Achar<DataGrid>("GridVendasSite");
        string? Ordem(string cabecalho) => grade.Columns.Single(c => c.Header?.ToString() == cabecalho).SortMemberPath;

        Assert.Equal("DataVenda", Ordem("Data"));
        Assert.Equal("Total", Ordem("Total"));
        Assert.Equal("Parcelas", Ordem("Parcelas"));
        Assert.Equal("SincronizadoEm", Ordem("Sincronizado em"));
    }

    [WpfFact]
    public void Botoes_EstaoLigadosAosComandos()
    {
        var m = new Montagem();
        var sincronizar = m.Achar<Button>("BtnSincronizar");
        var atualizar = m.Achar<Button>("BtnAtualizar");

        Assert.Same(m.Vm.SincronizarCommand, sincronizar.Command);
        Assert.Same(m.Vm.CarregarCommand, atualizar.Command);
        Assert.Equal("⇄  Sincronizar com o Site", sincronizar.Content);
        Assert.Contains("Atualizar", atualizar.Content.ToString());
    }

    [WpfFact]
    public void CampoDeBusca_EstaLigadoAoTermo()
    {
        var m = new Montagem();
        var busca = m.Achar<TextBox>("CampoBusca");

        busca.Text = "maria";

        Assert.Equal("maria", m.Vm.TermoBusca);
    }

    // ---------------------------------------------------------------------------------------
    // Estados
    // ---------------------------------------------------------------------------------------

    [WpfFact]
    public void AoAbrir_NenhumEstadoEhMostrado()
    {
        var m = new Montagem();

        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoVazio"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoErro"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoIntegracao"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoCarregando"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("BannerSincronizacao"));
    }

    [WpfFact]
    public async Task ComVendas_AGradeMostraAsLinhas_ENenhumEstadoApareceSobreEla()
    {
        var m = new Montagem(Pagina(2, Venda("IC-2026-000001", "20261006-0001"), Venda("IC-2026-000002", "20261006-0002")));

        await m.Vm.CarregarAsync();
        m.Medir(1366);

        var grade = m.Achar<DataGrid>("GridVendasSite");
        Assert.Equal(2, grade.Items.Count);
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoVazio"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoErro"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoIntegracao"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoCarregando"));
    }

    [WpfFact]
    public async Task SemVendas_MostraSoOEstadoVazio()
    {
        var m = new Montagem(Pagina(0));

        await m.Vm.CarregarAsync();

        Assert.Equal(Visibility.Visible, m.Visibilidade("EstadoVazio"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoErro"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoIntegracao"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoCarregando"));
    }

    [WpfFact]
    public async Task ErroAoCarregar_MostraOErroNaTela_ComBotaoParaTentarDeNovo()
    {
        var m = new Montagem();
        m.Vendas
            .Setup(s => s.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("banco fora do ar"));

        await m.Vm.CarregarAsync();

        Assert.Equal(Visibility.Visible, m.Visibilidade("EstadoErro"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoVazio"));
        var painel = m.Achar<StackPanel>("EstadoErro");
        var textos = painel.Children.OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(textos, t => t.Contains("banco fora do ar"));
        var tentarDeNovo = painel.Children.OfType<Button>().Single();
        Assert.Same(m.Vm.CarregarCommand, tentarDeNovo.Command);
        Assert.Equal("Tentar novamente", tentarDeNovo.Content);
    }

    [WpfFact]
    public async Task IntegracaoNaoInstalada_MostraAExplicacaoNaTela()
    {
        var m = new Montagem(new ResultadoVendasSiteDto { Situacao = SituacaoIntegracaoSite.NaoInstalada });

        await m.Vm.CarregarAsync();

        Assert.Equal(Visibility.Visible, m.Visibilidade("EstadoIntegracao"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoVazio"));
        var textos = m.Achar<StackPanel>("EstadoIntegracao").Children.OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(textos, t => t.Contains("scripts SQL"));
    }

    [WpfFact]
    public void DuranteOCarregamento_MostraOIndicadorDeCarregando()
    {
        var m = new Montagem();
        var tcs = new TaskCompletionSource<ResultadoVendasSiteDto>();
        m.Vendas
            .Setup(s => s.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var carga = m.Vm.CarregarAsync();

        Assert.Equal(Visibility.Visible, m.Visibilidade("EstadoCarregando"));
        var texto = m.Achar<Border>("EstadoCarregando").Child as StackPanel;
        Assert.Contains(texto!.Children.OfType<TextBlock>(), t => t.Text.Contains("Carregando vendas do site"));

        tcs.SetResult(Pagina(0));
        carga.GetAwaiter().GetResult();

        Assert.Equal(Visibility.Collapsed, m.Visibilidade("EstadoCarregando"));
    }

    // ---------------------------------------------------------------------------------------
    // Sincronização
    // ---------------------------------------------------------------------------------------

    [WpfFact]
    public async Task Sincronizacao_MostraAFaixaVerdeComAMensagemDePedida()
    {
        var m = new Montagem();

        await m.Vm.SincronizarAsync();

        Assert.Equal(Visibility.Visible, m.Visibilidade("BannerSincronizacao"));
        Assert.Equal("Sincronização concluída com sucesso.", m.Achar<TextBlock>("TextoSincronizacao").Text);
        Assert.Equal(Color.FromRgb(0xD4, 0xED, 0xDA), CorDoFundo(m.Achar<Border>("BannerSincronizacao")));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("BarraSincronizacao"));
    }

    [WpfFact]
    public async Task Sincronizacao_ComFalha_MostraAFaixaVermelha()
    {
        var m = new Montagem();
        m.Sincronizacao
            .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoSincronizacaoSite
            {
                Status = StatusSincronizacaoSite.ExecutavelNaoEncontrado,
                Mensagem = "ImperialSync.exe não foi encontrado na pasta do sistema."
            });

        await m.Vm.SincronizarAsync();

        Assert.Equal("ImperialSync.exe não foi encontrado na pasta do sistema.", m.Achar<TextBlock>("TextoSincronizacao").Text);
        Assert.Equal(Color.FromRgb(0xF8, 0xD7, 0xDA), CorDoFundo(m.Achar<Border>("BannerSincronizacao")));
    }

    [WpfFact]
    public async Task Sincronizacao_ComAtencao_MostraAFaixaAmarela()
    {
        var m = new Montagem();
        m.Sincronizacao
            .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoSincronizacaoSite
            {
                Status = StatusSincronizacaoSite.ConcluidaComAtencao,
                CodigoSaida = 10,
                Mensagem = "há vendas que precisam de atenção",
                ProcessoExecutado = true
            });

        await m.Vm.SincronizarAsync();

        Assert.Equal(Color.FromRgb(0xFF, 0xF3, 0xCD), CorDoFundo(m.Achar<Border>("BannerSincronizacao")));
    }

    // "A API aceitou" não é "sincronizou": produto da loja sem cadastro no site é faixa AMARELA.

    private static ResultadoSincronizacaoSite DoImperialSync(int codigo, ResumoEstoqueSite? resumo)
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.Interpretar(codigo, resumo);
        return new ResultadoSincronizacaoSite
        {
            Status = status, CodigoSaida = codigo, Mensagem = mensagem, ProcessoExecutado = true, ResumoEstoque = resumo
        };
    }

    private static readonly Color Amarelo = Color.FromRgb(0xFF, 0xF3, 0xCD);
    private static readonly Color Verde = Color.FromRgb(0xD4, 0xED, 0xDA);
    private static readonly Color Vermelho = Color.FromRgb(0xF8, 0xD7, 0xDA);
    private static readonly Color TextoDeAlerta = Color.FromRgb(0x85, 0x64, 0x04);

    [WpfTheory]
    [InlineData(14)]  // ImperialSync atual
    [InlineData(0)]   // ImperialSync antigo: saía com 0 e a faixa ficava verde
    public async Task NenhumSkuReconhecido_FaixaAmarela_ComNumerosAmostraEOrientacao(int codigo)
    {
        var m = new Montagem();
        m.Sincronizacao
            .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DoImperialSync(codigo, new ResumoEstoqueSite
            {
                Recebidos = 222, Atualizados = 0, SemCadastro = 222, AmostraSemCadastro = ["21201050", "301010001", "DIL001"]
            }));

        await m.Vm.SincronizarAsync();

        Assert.Equal(Amarelo, CorDoFundo(m.Achar<Border>("BannerSincronizacao")));
        Assert.Equal(
            "Sincronização concluída com alerta. 222 produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site.",
            m.Achar<TextBlock>("TextoSincronizacao").Text);

        var resumo = m.Achar<TextBlock>("ResumoEstoqueSincronizacao");
        var amostra = m.Achar<TextBlock>("AmostraSemCadastroSincronizacao");
        var orientacao = m.Achar<TextBlock>("OrientacaoEstoqueSincronizacao");
        Assert.All(new[] { resumo, amostra, orientacao }, linha =>
        {
            Assert.Equal(Visibility.Visible, linha.Visibility);
            Assert.Equal(TextoDeAlerta, ((SolidColorBrush)linha.Foreground).Color);
            Assert.Equal(TextWrapping.Wrap, linha.TextWrapping);
        });
        Assert.Equal("Recebidos pelo site: 222  ·  Atualizados: 0  ·  SKUs sem cadastro: 222", resumo.Text);
        Assert.Equal("Exemplos de SKUs sem cadastro (3 de 222): 21201050, 301010001, DIL001", amostra.Text);
        Assert.Contains("cadastre-os no site com o SKU igual ao Código do Produto", orientacao.Text);
        // Terminou: nada de barra de progresso e o botão volta.
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("BarraSincronizacao"));
        Assert.True(m.Achar<Button>("BtnSincronizar").Command.CanExecute(null));
    }

    [WpfFact]
    public async Task ParteDosSkusSemCadastro_FaixaAmarela_ComAContaParcial()
    {
        var m = new Montagem();
        m.Sincronizacao
            .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DoImperialSync(15, new ResumoEstoqueSite
            {
                Recebidos = 222, Atualizados = 200, SemCadastro = 22, AmostraSemCadastro = ["P00201", "P00202"]
            }));

        await m.Vm.SincronizarAsync();

        Assert.Equal(Amarelo, CorDoFundo(m.Achar<Border>("BannerSincronizacao")));
        Assert.Equal(
            "Sincronização concluída com alerta. 22 dos 222 produtos enviados não têm cadastro no catálogo do site.",
            m.Achar<TextBlock>("TextoSincronizacao").Text);
        Assert.Equal(
            "Recebidos pelo site: 222  ·  Atualizados: 200  ·  SKUs sem cadastro: 22",
            m.Achar<TextBlock>("ResumoEstoqueSincronizacao").Text);
        Assert.Equal(Visibility.Visible, m.Visibilidade("AmostraSemCadastroSincronizacao"));
        Assert.Equal(Visibility.Visible, m.Visibilidade("OrientacaoEstoqueSincronizacao"));
    }

    [WpfFact]
    public async Task TodosOsSkusReconhecidos_FaixaVerde_SoComOsNumeros()
    {
        var m = new Montagem();
        m.Sincronizacao
            .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DoImperialSync(0, new ResumoEstoqueSite { Recebidos = 222, Atualizados = 222, SemCadastro = 0 }));

        await m.Vm.SincronizarAsync();

        Assert.Equal(Verde, CorDoFundo(m.Achar<Border>("BannerSincronizacao")));
        Assert.Equal("Sincronização concluída com sucesso.", m.Achar<TextBlock>("TextoSincronizacao").Text);
        Assert.Equal(Visibility.Visible, m.Visibilidade("ResumoEstoqueSincronizacao"));
        Assert.Equal(
            "Recebidos pelo site: 222  ·  Atualizados: 222  ·  SKUs sem cadastro: 0",
            m.Achar<TextBlock>("ResumoEstoqueSincronizacao").Text);
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("AmostraSemCadastroSincronizacao"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("OrientacaoEstoqueSincronizacao"));
    }

    [WpfTheory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(12)]
    public async Task FalhaDeBancoHttpOuAssinatura_FaixaVermelha_SemLinhasDeEstoque(int codigo)
    {
        var m = new Montagem();
        m.Sincronizacao
            .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DoImperialSync(codigo, null));

        await m.Vm.SincronizarAsync();

        Assert.Equal(Vermelho, CorDoFundo(m.Achar<Border>("BannerSincronizacao")));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("ResumoEstoqueSincronizacao"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("AmostraSemCadastroSincronizacao"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("OrientacaoEstoqueSincronizacao"));
    }

    [WpfFact]
    public async Task SemResumoDoEstoque_AsLinhasDeEstoqueNaoOcupamEspaco()
    {
        var m = new Montagem();

        await m.Vm.SincronizarAsync();

        Assert.Equal(Verde, CorDoFundo(m.Achar<Border>("BannerSincronizacao")));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("ResumoEstoqueSincronizacao"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("AmostraSemCadastroSincronizacao"));
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("OrientacaoEstoqueSincronizacao"));
    }

    [WpfTheory]
    [InlineData(794)]   // janela de 1024 px (mínima) menos o menu lateral
    [InlineData(1136)]
    [InlineData(1690)]
    public async Task FaixaDeAlertaComAmostraLonga_QuebraALinha_ENaoPassaDaLargura(double largura)
    {
        var m = new Montagem(Pagina(1, Venda("IC-2026-000001", "20261006-0001")));
        await m.Vm.CarregarAsync();
        // Dez códigos de 40 caracteres: bem mais largo que qualquer janela.
        var codigos = Enumerable.Range(1, 10).Select(n => $"CODIGO-DE-PRODUTO-BEM-COMPRIDO-NUMERO-{n:0000}").ToArray();
        m.Sincronizacao
            .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(DoImperialSync(14, new ResumoEstoqueSite
            {
                Recebidos = 1209, Atualizados = 0, SemCadastro = 1209, AmostraSemCadastro = codigos
            }));
        await m.Vm.SincronizarAsync();

        m.Medir(largura);

        var faixa = m.Achar<Border>("BannerSincronizacao");
        var amostra = m.Achar<TextBlock>("AmostraSemCadastroSincronizacao");
        Assert.True(faixa.ActualWidth <= largura - 60 + 0.5, $"a faixa passa da janela: {faixa.ActualWidth:F0}");
        Assert.True(amostra.ActualWidth <= faixa.ActualWidth, "a amostra sai da faixa");
        // Mais de uma linha de texto: quebrou em vez de cortar ou empurrar a tela.
        Assert.True(amostra.ActualHeight > amostra.FontSize * 2, $"a amostra não quebrou a linha ({amostra.ActualHeight:F0})");
        foreach (var nome in new[] { "TextoSincronizacao", "ResumoEstoqueSincronizacao", "OrientacaoEstoqueSincronizacao" })
        {
            var linha = m.Achar<TextBlock>(nome);
            Assert.True(linha.ActualWidth <= faixa.ActualWidth, $"{nome} sai da faixa");
            Assert.True(linha.ActualHeight > 0, $"{nome} não foi desenhado");
        }
    }

    [WpfFact]
    public async Task EnquantoSincroniza_MostraAFaixaDeProgresso_EOBotaoFicaBloqueado()
    {
        var m = new Montagem();
        var fim = new TaskCompletionSource<ResultadoSincronizacaoSite>();
        m.Sincronizacao.Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>())).Returns(fim.Task);
        var botao = m.Achar<Button>("BtnSincronizar");
        ProcessarFilaDeMensagens();
        Assert.True(botao.IsEnabled);

        var execucao = m.Vm.SincronizarAsync();
        ProcessarFilaDeMensagens();

        Assert.Equal(Visibility.Visible, m.Visibilidade("BannerSincronizacao"));
        Assert.Equal(Visibility.Visible, m.Visibilidade("BarraSincronizacao"));
        Assert.Equal("Sincronizando com o site...", m.Achar<TextBlock>("TextoSincronizacao").Text);
        Assert.Equal("Sincronizando...", botao.Content);
        Assert.Equal(Color.FromRgb(0xE8, 0xF1, 0xFB), CorDoFundo(m.Achar<Border>("BannerSincronizacao")));
        Assert.False(botao.Command.CanExecute(null));
        Assert.False(botao.IsEnabled);

        fim.SetResult(new ResultadoSincronizacaoSite
        {
            Status = StatusSincronizacaoSite.Concluida, CodigoSaida = 0,
            Mensagem = "Sincronização concluída com sucesso.", ProcessoExecutado = true
        });
        await execucao;
        ProcessarFilaDeMensagens();

        Assert.True(botao.IsEnabled);
        Assert.Equal("⇄  Sincronizar com o Site", botao.Content);
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("BarraSincronizacao"));
    }

    [WpfFact]
    public async Task Detalhes_SoApareceQuandoOProgramaEscreveuAlgo()
    {
        var m = new Montagem();
        await m.Vm.SincronizarAsync();
        Assert.Equal(Visibility.Collapsed, m.Visibilidade("DetalhesExecucao"));

        m.Sincronizacao
            .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoSincronizacaoSite
            {
                Status = StatusSincronizacaoSite.Falhou, CodigoSaida = 4, Mensagem = "falhou", ProcessoExecutado = true,
                Detalhes = ["Banco local: falha."]
            });
        await m.Vm.SincronizarAsync();

        Assert.Equal(Visibility.Visible, m.Visibilidade("DetalhesExecucao"));
        var caixa = (TextBox)m.Achar<Expander>("DetalhesExecucao").Content;
        Assert.True(caixa.IsReadOnly);
        Assert.Contains("Banco local: falha.", caixa.Text);
    }

    // ---------------------------------------------------------------------------------------
    // Responsividade: o menu lateral ocupa 230 px e a janela vai de 1024 px para cima
    // ---------------------------------------------------------------------------------------

    [WpfTheory]
    [InlineData(794)]   // janela de 1024 px (mínima) menos o menu lateral
    [InlineData(1050)]
    [InlineData(1136)]  // 1366 px
    [InlineData(1690)]  // 1920 px
    public async Task Cabecalho_CabeEmQualquerLargura_SemSobreporOsBotoes(double largura)
    {
        var m = new Montagem(Pagina(1, Venda("IC-2026-000001", "20261006-0001")));
        await m.Vm.CarregarAsync();
        await m.Vm.SincronizarAsync();

        m.Medir(largura);

        var atualizar = m.Achar<Button>("BtnAtualizar");
        var sincronizar = m.Achar<Button>("BtnSincronizar");
        double Esquerda(FrameworkElement e) => e.TranslatePoint(new Point(0, 0), m.Tela).X;
        double Direita(FrameworkElement e) => e.TranslatePoint(new Point(e.ActualWidth, 0), m.Tela).X;

        Assert.True(Direita(sincronizar) <= largura - 30 + 0.5, $"o botão sai da janela ({Direita(sincronizar):F0} > {largura - 30})");
        Assert.True(Direita(atualizar) <= Esquerda(sincronizar) + 0.5, "os botões se sobrepõem");
        Assert.True(sincronizar.ActualHeight > 20 && atualizar.ActualHeight > 20);

        var titulo = m.Tela.FindVisualChildren<TextBlock>().First(t => t.Text == "Vendas do Site");
        Assert.True(Direita(titulo) <= Esquerda(atualizar) + 0.5, "o título invade os botões");
    }

    [WpfTheory]
    [InlineData(794)]
    [InlineData(1136)]
    [InlineData(1690)]
    public async Task GradeEFaixa_NaoPassamDaLarguraDisponivel(double largura)
    {
        var m = new Montagem(Pagina(1, Venda("IC-2026-000001", "20261006-0001")));
        await m.Vm.CarregarAsync();
        m.Sincronizacao
            .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoSincronizacaoSite
            {
                Status = StatusSincronizacaoSite.Falhou, CodigoSaida = 4, ProcessoExecutado = true,
                Mensagem = "O ImperialSync não conseguiu acessar o banco de dados da loja. Confira se o PostgreSQL está ligado e se o usuário e a senha do ImperialSync.env estão certos.",
                Detalhes = ["Banco local: falha."]
            });
        await m.Vm.SincronizarAsync();

        m.Medir(largura);

        var grade = m.Achar<DataGrid>("GridVendasSite");
        var faixa = m.Achar<Border>("BannerSincronizacao");
        Assert.True(grade.ActualWidth <= largura - 60 + 0.5, $"a grade passa da janela: {grade.ActualWidth:F0}");
        Assert.True(faixa.ActualWidth <= largura - 60 + 0.5, $"a faixa passa da janela: {faixa.ActualWidth:F0}");
        Assert.True(faixa.ActualHeight > 0);
        // A mensagem longa quebra a linha em vez de sair da faixa.
        var texto = m.Achar<TextBlock>("TextoSincronizacao");
        Assert.True(texto.ActualWidth <= faixa.ActualWidth);
    }

    [WpfFact]
    public void ColunaCliente_AbsorveAFolgaDaJanela()
    {
        var m = new Montagem();
        var grade = m.Achar<DataGrid>("GridVendasSite");

        var cliente = grade.Columns.Single(c => c.Header?.ToString() == "Cliente");

        Assert.True(cliente.Width.IsStar);
        Assert.All(grade.Columns.Where(c => c != cliente), c => Assert.False(c.Width.IsStar));
    }
}

internal static class VisualTreeTesteExtensions
{
    public static IEnumerable<T> FindVisualChildren<T>(this DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is T alvo)
                yield return alvo;

            foreach (var neto in filho.FindVisualChildren<T>())
                yield return neto;
        }
    }
}
