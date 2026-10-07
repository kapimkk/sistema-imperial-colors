using System.Runtime.CompilerServices;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.ReadModels;
using ImperialColors.UI.ViewModels;
using Moq;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// Tela "Vendas Site": carregamento, busca, paginação, os estados (carregando, vazia, erro,
/// integração indisponível) e o botão "Sincronizar com o Site", que só dispara o ImperialSync.exe.
///
/// Nenhum destes cenários pode abrir caixa de diálogo: uma <c>MessageBox</c> numa thread de teste
/// ficaria esperando alguém clicar e o teste nunca terminaria — então "o teste termina" já é, por
/// si, a prova de que erros e avisos aparecem DENTRO da tela.
/// </summary>
public class VendasSiteViewModelTests
{
    public VendasSiteViewModelTests() => WpfTestBootstrap.Inicializar();

    // ---------------------------------------------------------------------------------------
    // Montagem
    // ---------------------------------------------------------------------------------------

    private sealed class Cenario
    {
        public Mock<IVendaSiteService> Vendas { get; } = new();
        public Mock<ISincronizacaoSiteService> Sincronizacao { get; } = new();
        public bool EmExecucao { get; set; }
        public ResultadoSincronizacaoSite? UltimoResultado { get; set; }
        public VendasSiteViewModel Vm { get; private set; } = null!;

        public Cenario(ResultadoVendasSiteDto? paginaInicial = null, bool emExecucao = false)
        {
            EmExecucao = emExecucao;
            Vendas
                .Setup(s => s.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(paginaInicial ?? Pagina(0));
            Sincronizacao.SetupGet(s => s.EmExecucao).Returns(() => EmExecucao);
            Sincronizacao.SetupGet(s => s.UltimoResultado).Returns(() => UltimoResultado);
            Sincronizacao.SetupGet(s => s.CaminhoExecutavel).Returns(@"C:\Sistema\ImperialSync.exe");
            Sincronizacao
                .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Resultado(StatusSincronizacaoSite.Concluida, 0, "Sincronização concluída com sucesso."));
            Vm = new VendasSiteViewModel(Vendas.Object, Sincronizacao.Object);
        }

        public void SincronizacaoDevolve(ResultadoSincronizacaoSite resultado)
            => Sincronizacao.Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(resultado);

        public void CargaDevolve(ResultadoVendasSiteDto pagina)
            => Vendas
                .Setup(s => s.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(pagina);

        public int Cargas => Vendas.Invocations.Count(i => i.Method.Name == nameof(IVendaSiteService.ObterPaginadoAsync));
        public int Sincronizacoes => Sincronizacao.Invocations.Count(i => i.Method.Name == nameof(ISincronizacaoSiteService.SincronizarAsync));

        public (int Pagina, int Itens, string? Busca) UltimaCarga()
        {
            var chamada = Vendas.Invocations.Last(i => i.Method.Name == nameof(IVendaSiteService.ObterPaginadoAsync));
            return ((int)chamada.Arguments[0], (int)chamada.Arguments[1], (string?)chamada.Arguments[2]);
        }
    }

    private static VendaSiteDto Venda(string pedido, string numero) => new()
    {
        OperacaoId = Guid.NewGuid(),
        PedidoSite = pedido,
        NumeroVenda = numero,
        VendaExiste = true,
        Cliente = "Cliente Teste",
        Total = 365.47m,
        Pagamento = "Pix",
        Parcelas = 1,
        StatusDescricao = "Finalizada",
        DataVenda = new DateTime(2026, 10, 6, 16, 45, 0),
        SincronizadoEm = new DateTime(2026, 10, 6, 16, 45, 56)
    };

    private static ResultadoVendasSiteDto Pagina(int total, params VendaSiteDto[] itens) => new()
    {
        Pagina = new PaginacaoResultadoDto<VendaSiteDto>
        {
            Itens = itens,
            PaginaAtual = 1,
            ItensPorPagina = VendasSiteViewModel.ItensPorPaginaPadrao,
            TotalItens = total
        }
    };

    private static ResultadoVendasSiteDto Indisponivel(SituacaoIntegracaoSite situacao) => new() { Situacao = situacao };

    private static ResultadoSincronizacaoSite Resultado(
        StatusSincronizacaoSite status, int? codigo, string mensagem,
        bool executado = true, IReadOnlyList<string>? detalhes = null, double segundos = 0.6,
        ResumoEstoqueSite? resumo = null) => new()
    {
        Status = status,
        CodigoSaida = codigo,
        Mensagem = mensagem,
        ProcessoExecutado = executado,
        Detalhes = detalhes ?? [],
        Duracao = TimeSpan.FromSeconds(segundos),
        ResumoEstoque = resumo
    };

    private static ResumoEstoqueSite Resumo(int recebidos, int atualizados, int semCadastro, params string[] amostra) => new()
    {
        Recebidos = recebidos,
        Atualizados = atualizados,
        SemCadastro = semCadastro,
        AmostraSemCadastro = amostra
    };

    /// <summary>O resultado como o serviço o monta: status e mensagem saem do código de saída e do
    /// resumo do estoque, pela mesma regra do programa (SincronizacaoSiteMensagens.Interpretar).</summary>
    private static ResultadoSincronizacaoSite DoImperialSync(int codigo, ResumoEstoqueSite? resumo)
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.Interpretar(codigo, resumo);
        return Resultado(status, codigo, mensagem, resumo: resumo);
    }

    // ---------------------------------------------------------------------------------------
    // Carregamento e estados da lista
    // ---------------------------------------------------------------------------------------

    [WpfFact]
    public async Task Carregar_MostraAsVendasEAPaginacao()
    {
        var c = new Cenario(Pagina(120, Venda("IC-2026-000001", "20261006-0001"), Venda("IC-2026-000002", "20261006-0002")));

        await c.Vm.CarregarAsync();

        Assert.Equal(["IC-2026-000001", "IC-2026-000002"], c.Vm.Vendas.Select(v => v.PedidoSite));
        Assert.Equal(120, c.Vm.TotalItens);
        Assert.Equal(3, c.Vm.TotalPaginas);
        Assert.Equal("Página 1 de 3 — 120 venda(s) do site", c.Vm.InfoPaginacao);
        Assert.True(c.Vm.TemVendas);
        Assert.False(c.Vm.Carregando);
        Assert.False(c.Vm.MostrarEstadoVazio);
        Assert.False(c.Vm.MostrarEstadoErro);
        Assert.False(c.Vm.MostrarIntegracaoIndisponivel);
        Assert.False(c.Vm.PodePaginaAnterior);
        Assert.True(c.Vm.PodePaginaProxima);
        Assert.Equal((1, VendasSiteViewModel.ItensPorPaginaPadrao, (string?)null), c.UltimaCarga());
    }

    [WpfFact]
    public void AntesDaPrimeiraCarga_NaoAfirmaQueNaoHaVendas()
    {
        var c = new Cenario();

        Assert.False(c.Vm.MostrarEstadoVazio);
        Assert.False(c.Vm.MostrarEstadoErro);
        Assert.False(c.Vm.MostrarIntegracaoIndisponivel);
        Assert.False(c.Vm.Sincronizando);
        Assert.False(c.Vm.TemMensagemSincronizacao);
        Assert.Equal(0, c.Cargas);
    }

    [WpfFact]
    public async Task SemNenhumaVenda_MostraOEstadoVazio()
    {
        var c = new Cenario(Pagina(0));

        await c.Vm.CarregarAsync();

        Assert.True(c.Vm.MostrarEstadoVazio);
        Assert.False(c.Vm.MostrarEstadoErro);
        Assert.False(c.Vm.MostrarIntegracaoIndisponivel);
        Assert.Equal("Nenhuma venda do site", c.Vm.InfoPaginacao);
    }

    [WpfFact]
    public async Task IntegracaoNaoInstalada_ExplicaOQueFazer_EnaoDizQueNaoHaVendas()
    {
        var c = new Cenario(Indisponivel(SituacaoIntegracaoSite.NaoInstalada));

        await c.Vm.CarregarAsync();

        Assert.True(c.Vm.IntegracaoIndisponivel);
        Assert.True(c.Vm.MostrarIntegracaoIndisponivel);
        Assert.False(c.Vm.MostrarEstadoVazio);
        Assert.Contains("ainda não foi instalada", c.Vm.MensagemIntegracao);
        Assert.Contains("scripts SQL", c.Vm.MensagemIntegracao);
    }

    [WpfFact]
    public async Task IntegracaoSemPermissao_DizQuemPrecisaLiberar()
    {
        var c = new Cenario(Indisponivel(SituacaoIntegracaoSite.SemPermissao));

        await c.Vm.CarregarAsync();

        Assert.True(c.Vm.MostrarIntegracaoIndisponivel);
        Assert.Contains("permissão", c.Vm.MensagemIntegracao);
        Assert.Contains("integration", c.Vm.MensagemIntegracao);
    }

    [WpfFact]
    public async Task ErroAoCarregar_FicaNaTela_SemCaixaDeDialogo_ESemLancar()
    {
        var c = new Cenario();
        c.Vendas
            .Setup(s => s.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("banco fora do ar"));

        await c.Vm.CarregarAsync();

        Assert.True(c.Vm.TemErroCarga);
        Assert.True(c.Vm.MostrarEstadoErro);
        Assert.Contains("Não foi possível carregar as vendas do site", c.Vm.MensagemErroCarga);
        Assert.Contains("banco fora do ar", c.Vm.MensagemErroCarga);
        Assert.False(c.Vm.Carregando);
        Assert.False(c.Vm.MostrarEstadoVazio);
        Assert.Empty(c.Vm.Vendas);
    }

    [WpfFact]
    public async Task DepoisDeUmErro_AtualizarRecuperaATela()
    {
        var c = new Cenario();
        c.Vendas
            .SetupSequence(s => s.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falhou"))
            .ReturnsAsync(Pagina(1, Venda("IC-2026-000001", "20261006-0001")));

        await c.Vm.CarregarAsync();
        Assert.True(c.Vm.MostrarEstadoErro);

        await c.Vm.CarregarAsync();

        Assert.False(c.Vm.TemErroCarga);
        Assert.False(c.Vm.MostrarEstadoErro);
        Assert.Equal(string.Empty, c.Vm.MensagemErroCarga);
        Assert.Single(c.Vm.Vendas);
    }

    [WpfFact]
    public async Task DuranteOCarregamento_MostraCarregando_ESomeDepois()
    {
        var c = new Cenario();
        var tcs = new TaskCompletionSource<ResultadoVendasSiteDto>();
        c.Vendas
            .Setup(s => s.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var carga = c.Vm.CarregarAsync();

        Assert.True(c.Vm.Carregando);
        Assert.True(c.Vm.MostrarCarregando);
        Assert.False(c.Vm.MostrarEstadoVazio);

        tcs.SetResult(Pagina(0));
        await carga;

        Assert.False(c.Vm.Carregando);
        Assert.False(c.Vm.MostrarCarregando);
        Assert.True(c.Vm.MostrarEstadoVazio);
    }

    // ---------------------------------------------------------------------------------------
    // Busca e paginação
    // ---------------------------------------------------------------------------------------

    [WpfFact]
    public async Task Busca_VaiParaOServicoAparada_VoltandoParaAPrimeiraPagina()
    {
        var c = new Cenario(Pagina(300, Venda("IC-2026-000001", "20261006-0001")));
        await c.Vm.CarregarAsync();
        c.Vm.PaginaAtual = 3;

        c.Vm.TermoBusca = "  maria  ";
        await c.Vm.CarregarAsync();

        Assert.Equal((1, VendasSiteViewModel.ItensPorPaginaPadrao, (string?)"maria"), c.UltimaCarga());
        Assert.Equal(1, c.Vm.PaginaAtual);
    }

    [WpfFact]
    public async Task Busca_ApagadaVoltaASemFiltro()
    {
        var c = new Cenario(Pagina(1, Venda("IC-2026-000001", "20261006-0001")));
        c.Vm.TermoBusca = "maria";
        await c.Vm.CarregarAsync();

        c.Vm.TermoBusca = "   ";
        await c.Vm.CarregarAsync();

        Assert.Null(c.UltimaCarga().Busca);
    }

    [WpfFact]
    public async Task Busca_IgualAAnterior_NaoConsultaDeNovo()
    {
        var c = new Cenario(Pagina(1, Venda("IC-2026-000001", "20261006-0001")));
        c.Vm.TermoBusca = "maria";
        await c.Vm.CarregarAsync();
        var antes = c.Cargas;

        c.Vm.TermoBusca = "maria";

        Assert.Equal(antes, c.Cargas);
    }

    [WpfFact]
    public async Task Paginacao_ProximaEAnterior()
    {
        var c = new Cenario(Pagina(120, Venda("IC-2026-000001", "20261006-0001")));
        await c.Vm.CarregarAsync();

        await c.Vm.PaginaProximaCommandParaTeste();
        Assert.Equal(2, c.Vm.PaginaAtual);
        Assert.Equal(2, c.UltimaCarga().Pagina);
        Assert.True(c.Vm.PodePaginaAnterior);

        await c.Vm.PaginaAnteriorCommandParaTeste();
        Assert.Equal(1, c.Vm.PaginaAtual);
        Assert.Equal(1, c.UltimaCarga().Pagina);
    }

    [WpfFact]
    public async Task Paginacao_NaoPassaDoLimite()
    {
        var c = new Cenario(Pagina(10, Venda("IC-2026-000001", "20261006-0001")));
        await c.Vm.CarregarAsync();
        var antes = c.Cargas;

        await c.Vm.PaginaProximaCommandParaTeste();
        await c.Vm.PaginaAnteriorCommandParaTeste();

        Assert.Equal(1, c.Vm.PaginaAtual);
        Assert.Equal(antes, c.Cargas);
        Assert.False(c.Vm.PaginaProximaCommand.CanExecute(null));
        Assert.False(c.Vm.PaginaAnteriorCommand.CanExecute(null));
    }

    // ---------------------------------------------------------------------------------------
    // Sincronizar com o Site
    // ---------------------------------------------------------------------------------------

    [WpfFact]
    public async Task Sincronizar_Sucesso_MostraAMensagem_ERecarregaAsVendas()
    {
        var c = new Cenario(Pagina(1, Venda("IC-2026-000001", "20261006-0001")));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(Resultado(
            StatusSincronizacaoSite.Concluida, 0, "Sincronização concluída com sucesso.",
            detalhes: ["Vendas: 2 criada(s) na loja, 0 já existia(m), 0 recusada(s).", "Duração: 0,1 s"], segundos: 1.5));
        var cargasAntes = c.Cargas;

        await c.Vm.SincronizarAsync();

        Assert.False(c.Vm.Sincronizando);
        Assert.Equal("Sincronização concluída com sucesso.", c.Vm.MensagemSincronizacao);
        Assert.Equal(GravidadeSincronizacao.Sucesso, c.Vm.GravidadeSincronizacao);
        Assert.Equal(1, c.Sincronizacoes);
        Assert.Equal(cargasAntes + 1, c.Cargas);
        Assert.Contains("código de saída 0", c.Vm.RodapeSincronizacao);
        Assert.Contains("1,5 s", c.Vm.RodapeSincronizacao);
        Assert.Contains("Vendas: 2 criada(s) na loja", c.Vm.DetalhesSincronizacao);
        Assert.True(c.Vm.SincronizarCommand.CanExecute(null));
    }

    [WpfFact]
    public async Task Sincronizar_ExecutavelNaoEncontrado_AvisaEmVezDeFalharEmSilencio_ENaoRecarrega()
    {
        var c = new Cenario(Pagina(1, Venda("IC-2026-000001", "20261006-0001")));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(Resultado(
            StatusSincronizacaoSite.ExecutavelNaoEncontrado, null, "ImperialSync.exe não foi encontrado na pasta do sistema.", executado: false));
        var cargasAntes = c.Cargas;

        await c.Vm.SincronizarAsync();

        Assert.Equal("ImperialSync.exe não foi encontrado na pasta do sistema.", c.Vm.MensagemSincronizacao);
        Assert.Equal(GravidadeSincronizacao.Erro, c.Vm.GravidadeSincronizacao);
        Assert.False(c.Vm.Sincronizando);
        Assert.Equal(cargasAntes, c.Cargas);
        Assert.Equal(string.Empty, c.Vm.RodapeSincronizacao);
        Assert.Equal(string.Empty, c.Vm.DetalhesSincronizacao);
    }

    [WpfTheory]
    [InlineData(StatusSincronizacaoSite.ConcluidaComAtencao, 10, GravidadeSincronizacao.Atencao)]
    [InlineData(StatusSincronizacaoSite.Falhou, 4, GravidadeSincronizacao.Erro)]
    [InlineData(StatusSincronizacaoSite.Falhou, 6, GravidadeSincronizacao.Erro)]
    [InlineData(StatusSincronizacaoSite.Falhou, 11, GravidadeSincronizacao.Erro)]
    [InlineData(StatusSincronizacaoSite.TempoEsgotado, null, GravidadeSincronizacao.Erro)]
    [InlineData(StatusSincronizacaoSite.NaoIniciou, null, GravidadeSincronizacao.Erro)]
    [InlineData(StatusSincronizacaoSite.Cancelada, 9, GravidadeSincronizacao.Atencao)]
    public async Task Sincronizar_ComOutrosResultados_UsaAGravidadeCerta_ERecarregaSeOProgramaRodou(
        StatusSincronizacaoSite status, int? codigo, GravidadeSincronizacao gravidade)
    {
        var executado = status != StatusSincronizacaoSite.NaoIniciou;
        var c = new Cenario(Pagina(0));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(Resultado(status, codigo, $"mensagem para {status}", executado, ["linha do programa"]));
        var cargasAntes = c.Cargas;

        await c.Vm.SincronizarAsync();

        Assert.Equal(gravidade, c.Vm.GravidadeSincronizacao);
        Assert.Equal($"mensagem para {status}", c.Vm.MensagemSincronizacao);
        Assert.Equal(executado ? cargasAntes + 1 : cargasAntes, c.Cargas);
        Assert.False(c.Vm.Sincronizando);
        Assert.Equal(executado, c.Vm.TemRodapeSincronizacao);
    }

    // ---------------------------------------------------------------------------------------
    // Estoque: produto sem cadastro no site é ALERTA (amarelo), nunca sucesso (verde) nem erro
    // ---------------------------------------------------------------------------------------

    [WpfTheory]
    [InlineData(14)]  // ImperialSync atual
    [InlineData(0)]   // ImperialSync antigo, que terminava com 0 neste cenário
    public async Task Recebidos222_Atualizados0_SemCadastro222_FaixaDeAlerta_ComNumerosAmostraEOrientacao(int codigo)
    {
        var c = new Cenario(Pagina(0));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(DoImperialSync(codigo, Resumo(222, 0, 222, "21201050", "301010001", "DIL001")));
        var cargasAntes = c.Cargas;

        await c.Vm.SincronizarAsync();

        Assert.Equal(GravidadeSincronizacao.Atencao, c.Vm.GravidadeSincronizacao);
        Assert.Equal(
            "Sincronização concluída com alerta. 222 produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site.",
            c.Vm.MensagemSincronizacao);
        Assert.True(c.Vm.TemResumoEstoque);
        Assert.Equal("Recebidos pelo site: 222  ·  Atualizados: 0  ·  SKUs sem cadastro: 222", c.Vm.ResumoEstoque);
        Assert.True(c.Vm.TemAmostraSemCadastro);
        Assert.Equal("Exemplos de SKUs sem cadastro (3 de 222): 21201050, 301010001, DIL001", c.Vm.AmostraSemCadastro);
        Assert.True(c.Vm.TemOrientacaoEstoque);
        Assert.Contains("Código do Produto", c.Vm.OrientacaoEstoque);
        Assert.Contains($"código de saída {codigo}", c.Vm.RodapeSincronizacao);
        // Não é falha: a lista é recarregada e o botão volta a funcionar.
        Assert.Equal(cargasAntes + 1, c.Cargas);
        Assert.False(c.Vm.Sincronizando);
        Assert.True(c.Vm.SincronizarCommand.CanExecute(null));
    }

    [WpfFact]
    public async Task Recebidos222_Atualizados200_SemCadastro22_FaixaDeAlertaParcial()
    {
        var c = new Cenario(Pagina(0));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(DoImperialSync(15, Resumo(222, 200, 22, "P00201", "P00202")));

        await c.Vm.SincronizarAsync();

        Assert.Equal(GravidadeSincronizacao.Atencao, c.Vm.GravidadeSincronizacao);
        Assert.Equal(
            "Sincronização concluída com alerta. 22 dos 222 produtos enviados não têm cadastro no catálogo do site.",
            c.Vm.MensagemSincronizacao);
        Assert.Equal("Recebidos pelo site: 222  ·  Atualizados: 200  ·  SKUs sem cadastro: 22", c.Vm.ResumoEstoque);
        Assert.Equal("Exemplos de SKUs sem cadastro (2 de 22): P00201, P00202", c.Vm.AmostraSemCadastro);
        Assert.True(c.Vm.TemOrientacaoEstoque);
    }

    [WpfFact]
    public async Task Recebidos222_TodosReconhecidos_FaixaDeSucesso_ComOsNumeros_SemAlerta()
    {
        var c = new Cenario(Pagina(0));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(DoImperialSync(0, Resumo(222, 222, 0)));

        await c.Vm.SincronizarAsync();

        Assert.Equal(GravidadeSincronizacao.Sucesso, c.Vm.GravidadeSincronizacao);
        Assert.Equal("Sincronização concluída com sucesso.", c.Vm.MensagemSincronizacao);
        Assert.Equal("Recebidos pelo site: 222  ·  Atualizados: 222  ·  SKUs sem cadastro: 0", c.Vm.ResumoEstoque);
        Assert.False(c.Vm.TemAmostraSemCadastro);
        Assert.False(c.Vm.TemOrientacaoEstoque);
    }

    [WpfTheory]
    [InlineData(4)]   // banco da loja
    [InlineData(5)]   // API recusou (assinatura/HMAC)
    [InlineData(6)]   // API indisponível (HTTP)
    [InlineData(12)]  // resposta sem assinatura válida
    public async Task FalhaDeBancoHttpOuAssinatura_FaixaDeErro_NuncaAlertaNemSucesso(int codigo)
    {
        var c = new Cenario(Pagina(0));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(DoImperialSync(codigo, null));

        await c.Vm.SincronizarAsync();

        Assert.Equal(GravidadeSincronizacao.Erro, c.Vm.GravidadeSincronizacao);
        Assert.DoesNotContain("concluída", c.Vm.MensagemSincronizacao);
        Assert.False(c.Vm.TemResumoEstoque);
        Assert.False(c.Vm.TemAmostraSemCadastro);
        Assert.False(c.Vm.TemOrientacaoEstoque);
    }

    [WpfFact]
    public async Task FalhaComResumoDeProdutosSemCadastro_ContinuaErro_MostraOsNumerosSemAOrientacao()
    {
        // As vendas falharam (código 5) depois de o estoque ter ido com produtos sem cadastro.
        var c = new Cenario(Pagina(0));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(DoImperialSync(5, Resumo(222, 0, 222, "A-1")));

        await c.Vm.SincronizarAsync();

        Assert.Equal(GravidadeSincronizacao.Erro, c.Vm.GravidadeSincronizacao);
        Assert.Contains("recusou", c.Vm.MensagemSincronizacao);
        Assert.True(c.Vm.TemResumoEstoque);
        Assert.True(c.Vm.TemAmostraSemCadastro);
        // Primeiro a falha: a orientação de cadastro só aparece quando a rodada termina em alerta.
        Assert.False(c.Vm.TemOrientacaoEstoque);
    }

    [WpfFact]
    public async Task AlertaPeloCodigoSemOsNumeros_AindaOrientaOOperador()
    {
        var c = new Cenario(Pagina(0));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(DoImperialSync(14, null));

        await c.Vm.SincronizarAsync();

        Assert.Equal(GravidadeSincronizacao.Atencao, c.Vm.GravidadeSincronizacao);
        Assert.StartsWith("Sincronização concluída com alerta.", c.Vm.MensagemSincronizacao);
        Assert.False(c.Vm.TemResumoEstoque);
        Assert.True(c.Vm.TemOrientacaoEstoque);
    }

    [WpfFact]
    public async Task NovaSincronizacao_LimpaOResumoDoEstoqueDaAnterior()
    {
        var c = new Cenario(Pagina(0));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(DoImperialSync(14, Resumo(222, 0, 222, "A-1")));
        await c.Vm.SincronizarAsync();
        Assert.True(c.Vm.TemResumoEstoque);

        // A próxima termina sem resumo (falhou antes de chegar ao estoque): nada da anterior sobra.
        c.SincronizacaoDevolve(DoImperialSync(6, null));
        await c.Vm.SincronizarAsync();

        Assert.Equal(GravidadeSincronizacao.Erro, c.Vm.GravidadeSincronizacao);
        Assert.Equal(string.Empty, c.Vm.ResumoEstoque);
        Assert.Equal(string.Empty, c.Vm.AmostraSemCadastro);
        Assert.Equal(string.Empty, c.Vm.OrientacaoEstoque);
    }

    [WpfFact]
    public async Task EnquantoSincroniza_OResumoDaRodadaAnteriorNaoFicaNaTela()
    {
        var c = new Cenario(Pagina(0));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(DoImperialSync(15, Resumo(222, 200, 22, "A-1")));
        await c.Vm.SincronizarAsync();

        var emAndamento = new TaskCompletionSource<ResultadoSincronizacaoSite>();
        c.Sincronizacao.Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>())).Returns(emAndamento.Task);
        var execucao = c.Vm.SincronizarAsync();

        Assert.True(c.Vm.Sincronizando);
        Assert.Equal(GravidadeSincronizacao.Informacao, c.Vm.GravidadeSincronizacao);
        Assert.False(c.Vm.TemResumoEstoque);
        Assert.False(c.Vm.TemAmostraSemCadastro);
        Assert.False(c.Vm.TemOrientacaoEstoque);

        emAndamento.SetResult(DoImperialSync(0, Resumo(222, 222, 0)));
        await execucao;

        Assert.Equal(GravidadeSincronizacao.Sucesso, c.Vm.GravidadeSincronizacao);
        Assert.True(c.Vm.TemResumoEstoque);
    }

    [WpfFact]
    public async Task Sincronizar_QuandoOutraJaRoda_NaoRecarrega_EMostraAviso()
    {
        var c = new Cenario(Pagina(0));
        await c.Vm.CarregarAsync();
        c.SincronizacaoDevolve(Resultado(
            StatusSincronizacaoSite.JaEmExecucao, null, "Já existe uma sincronização em andamento. Aguarde ela terminar.", executado: false));
        var cargasAntes = c.Cargas;

        await c.Vm.SincronizarAsync();

        Assert.Equal(GravidadeSincronizacao.Atencao, c.Vm.GravidadeSincronizacao);
        Assert.Contains("andamento", c.Vm.MensagemSincronizacao);
        Assert.Equal(cargasAntes, c.Cargas);
    }

    [WpfFact]
    public async Task Sincronizar_EnquantoRoda_MostraSincronizando_BloqueiaOBotao_EDesbloqueiaNoFim()
    {
        var c = new Cenario(Pagina(0));
        var fim = new TaskCompletionSource<ResultadoSincronizacaoSite>();
        c.Sincronizacao.Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>())).Returns(fim.Task);
        Assert.True(c.Vm.SincronizarCommand.CanExecute(null));

        var execucao = c.Vm.SincronizarAsync();

        Assert.True(c.Vm.Sincronizando);
        Assert.Equal("Sincronizando com o site...", c.Vm.MensagemSincronizacao);
        Assert.Equal(GravidadeSincronizacao.Informacao, c.Vm.GravidadeSincronizacao);
        Assert.Equal("Sincronizando...", c.Vm.TextoBotaoSincronizar);
        Assert.False(c.Vm.SincronizarCommand.CanExecute(null));

        fim.SetResult(Resultado(StatusSincronizacaoSite.Concluida, 0, "Sincronização concluída com sucesso."));
        await execucao;

        Assert.False(c.Vm.Sincronizando);
        Assert.Equal("⇄  Sincronizar com o Site", c.Vm.TextoBotaoSincronizar);
        Assert.True(c.Vm.SincronizarCommand.CanExecute(null));
        Assert.Equal("Sincronização concluída com sucesso.", c.Vm.MensagemSincronizacao);
    }

    [WpfFact]
    public async Task SegundaExecucaoEnquantoRoda_NaoChamaOServicoDeNovo()
    {
        var c = new Cenario(Pagina(0));
        var fim = new TaskCompletionSource<ResultadoSincronizacaoSite>();
        c.Sincronizacao.Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>())).Returns(fim.Task);

        var primeira = c.Vm.SincronizarAsync();
        await c.Vm.SincronizarAsync();
        c.Vm.SincronizarCommand.Execute(null);

        Assert.Equal(1, c.Sincronizacoes);

        fim.SetResult(Resultado(StatusSincronizacaoSite.Concluida, 0, "Sincronização concluída com sucesso."));
        await primeira;
        Assert.Equal(1, c.Sincronizacoes);
    }

    [WpfFact]
    public async Task ComandoDoBotao_ExecutaAMesmaRotina()
    {
        var c = new Cenario(Pagina(0));

        c.Vm.SincronizarCommand.Execute(null);

        // O serviço de mentira responde na hora, então o comando já terminou.
        Assert.Equal(1, c.Sincronizacoes);
        Assert.Equal("Sincronização concluída com sucesso.", c.Vm.MensagemSincronizacao);
        Assert.Equal(1, c.Cargas);
        await Task.CompletedTask;
    }

    [WpfFact]
    public async Task ServicoQueLanca_NaoDeixaATelaPresaEmSincronizando()
    {
        var c = new Cenario(Pagina(0));
        c.Sincronizacao
            .Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("estourou"));

        await c.Vm.SincronizarAsync();

        Assert.False(c.Vm.Sincronizando);
        Assert.Equal(GravidadeSincronizacao.Erro, c.Vm.GravidadeSincronizacao);
        Assert.Contains("erro inesperado", c.Vm.MensagemSincronizacao);
        Assert.DoesNotContain("estourou", c.Vm.MensagemSincronizacao);
        Assert.True(c.Vm.SincronizarCommand.CanExecute(null));
    }

    [WpfFact]
    public async Task Detalhes_MostramNoMaximoAsUltimasQuarentaLinhas()
    {
        var c = new Cenario(Pagina(0));
        c.SincronizacaoDevolve(Resultado(
            StatusSincronizacaoSite.Concluida, 0, "Sincronização concluída com sucesso.",
            detalhes: Enumerable.Range(1, 100).Select(n => $"linha {n}").ToList()));

        await c.Vm.SincronizarAsync();

        var linhas = c.Vm.DetalhesSincronizacao.Split(Environment.NewLine);
        Assert.Equal(40, linhas.Length);
        Assert.Equal("linha 61", linhas[0]);
        Assert.Equal("linha 100", linhas[^1]);
    }

    [WpfFact]
    public async Task NovaSincronizacao_LimpaOResultadoDaAnteriorEnquantoRoda()
    {
        var c = new Cenario(Pagina(0));
        c.SincronizacaoDevolve(Resultado(StatusSincronizacaoSite.Falhou, 4, "falhou antes", detalhes: ["detalhe antigo"]));
        await c.Vm.SincronizarAsync();
        Assert.Equal("falhou antes", c.Vm.MensagemSincronizacao);

        var fim = new TaskCompletionSource<ResultadoSincronizacaoSite>();
        c.Sincronizacao.Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>())).Returns(fim.Task);
        var execucao = c.Vm.SincronizarAsync();

        Assert.Equal("Sincronizando com o site...", c.Vm.MensagemSincronizacao);
        Assert.Equal(string.Empty, c.Vm.DetalhesSincronizacao);
        Assert.Equal(string.Empty, c.Vm.RodapeSincronizacao);

        fim.SetResult(Resultado(StatusSincronizacaoSite.Concluida, 0, "Sincronização concluída com sucesso."));
        await execucao;
    }

    // ---------------------------------------------------------------------------------------
    // Sincronização iniciada por outra tela (o serviço é um só)
    // ---------------------------------------------------------------------------------------

    [WpfFact]
    public void TelaQueNasceComSincronizacaoRodando_JaMostraEBloqueia()
    {
        var c = new Cenario(Pagina(0), emExecucao: true);

        Assert.True(c.Vm.Sincronizando);
        Assert.Equal("Sincronizando com o site...", c.Vm.MensagemSincronizacao);
        Assert.False(c.Vm.SincronizarCommand.CanExecute(null));
    }

    [WpfFact]
    public async Task TelaAcompanhaOFimDeUmaSincronizacaoIniciadaAntes_ERecarrega()
    {
        var c = new Cenario(Pagina(0), emExecucao: true);
        await c.Vm.CarregarAsync();
        var cargasAntes = c.Cargas;

        c.EmExecucao = false;
        c.UltimoResultado = Resultado(StatusSincronizacaoSite.Concluida, 0, "Sincronização concluída com sucesso.");
        c.Sincronizacao.Raise(s => s.EmExecucaoAlterada += null, EventArgs.Empty);

        Assert.False(c.Vm.Sincronizando);
        Assert.Equal("Sincronização concluída com sucesso.", c.Vm.MensagemSincronizacao);
        Assert.Equal(GravidadeSincronizacao.Sucesso, c.Vm.GravidadeSincronizacao);
        Assert.Equal(cargasAntes + 1, c.Cargas);
        Assert.True(c.Vm.SincronizarCommand.CanExecute(null));
    }

    [WpfFact]
    public void TelaAcompanhaOInicioDeUmaSincronizacaoDeOutraTela()
    {
        var c = new Cenario(Pagina(0));
        Assert.False(c.Vm.Sincronizando);

        c.EmExecucao = true;
        c.Sincronizacao.Raise(s => s.EmExecucaoAlterada += null, EventArgs.Empty);

        Assert.True(c.Vm.Sincronizando);
        Assert.Equal("Sincronizando com o site...", c.Vm.MensagemSincronizacao);
        Assert.False(c.Vm.SincronizarCommand.CanExecute(null));
    }

    [WpfFact]
    public async Task AoTerminarASincronizacaoDestaTela_OAvisoDoServicoNaoDuplicaARecarga()
    {
        var c = new Cenario(Pagina(0));
        var fim = new TaskCompletionSource<ResultadoSincronizacaoSite>();
        c.Sincronizacao.Setup(s => s.SincronizarAsync(It.IsAny<CancellationToken>())).Returns(fim.Task);
        var execucao = c.Vm.SincronizarAsync();

        // O serviço avisa que terminou ANTES de a continuação da tela rodar.
        c.EmExecucao = false;
        c.UltimoResultado = Resultado(StatusSincronizacaoSite.Concluida, 0, "Sincronização concluída com sucesso.");
        c.Sincronizacao.Raise(s => s.EmExecucaoAlterada += null, EventArgs.Empty);
        fim.SetResult(c.UltimoResultado);
        await execucao;

        Assert.Equal(1, c.Cargas);
        Assert.False(c.Vm.Sincronizando);
    }

    [WpfFact]
    public void TelaDescartada_NaoFicaPresaAoServico()
    {
        var c = new Cenario(Pagina(0));
        var referencia = CriarETirarDeCircuito(c);

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(referencia.TryGetTarget(out _), "a tela descartada continua viva, presa ao serviço");

        // Avisar o serviço depois disso não pode falhar (o ouvinte órfão se remove sozinho).
        c.Sincronizacao.Raise(s => s.EmExecucaoAlterada += null, EventArgs.Empty);
        c.Sincronizacao.Raise(s => s.EmExecucaoAlterada += null, EventArgs.Empty);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<VendasSiteViewModel> CriarETirarDeCircuito(Cenario c)
        => new(new VendasSiteViewModel(c.Vendas.Object, c.Sincronizacao.Object));
}

/// <summary>Os comandos de paginação são <c>async void</c> por baixo; o teste precisa esperar o fim.</summary>
internal static class VendasSiteViewModelTesteExtensions
{
    public static async Task PaginaProximaCommandParaTeste(this VendasSiteViewModel vm)
    {
        if (vm.PaginaProximaCommand.CanExecute(null))
            vm.PaginaProximaCommand.Execute(null);

        await Task.Yield();
    }

    public static async Task PaginaAnteriorCommandParaTeste(this VendasSiteViewModel vm)
    {
        if (vm.PaginaAnteriorCommand.CanExecute(null))
            vm.PaginaAnteriorCommand.Execute(null);

        await Task.Yield();
    }
}
