using System.Collections.ObjectModel;
using System.Globalization;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.ReadModels;
using ImperialColors.UI.Helpers;

namespace ImperialColors.UI.ViewModels;

/// <summary>Como a faixa de mensagem da sincronização é pintada.</summary>
public enum GravidadeSincronizacao
{
    Nenhuma = 0,
    Informacao = 1,
    Sucesso = 2,
    Atencao = 3,
    Erro = 4
}

/// <summary>
/// Tela "Vendas Site": as vendas que o ImperialSync criou neste banco a partir de pedidos pagos
/// no e-commerce, e o botão "Sincronizar com o Site".
///
/// O botão só <b>dispara o ImperialSync.exe</b> (<see cref="ISincronizacaoSiteService"/>) e
/// recarrega a lista quando ele termina. Este sistema não fala com o site, não assina nada e
/// não cria venda online: tudo isso é do ImperialSync.
///
/// Nenhum erro aqui abre caixa de diálogo: carregamento e sincronização mostram o resultado numa
/// faixa dentro da própria tela (e a lista continua utilizável), o que também deixa o
/// comportamento testável sem janela modal.
/// </summary>
public class VendasSiteViewModel : BaseViewModel
{
    public const int ItensPorPaginaPadrao = 50;

    private const int MaximoLinhasDetalhe = 40;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly IVendaSiteService _vendaSiteService;
    private readonly ISincronizacaoSiteService _sincronizacaoService;
    private CancellationTokenSource? _buscaCts;
    private readonly SemaphoreSlim _buscaSemaforo = new(1, 1);
    private bool _execucaoPropria;

    private ObservableCollection<VendaSiteDto> _vendas = new();
    public ObservableCollection<VendaSiteDto> Vendas
    {
        get => _vendas;
        private set
        {
            SetProperty(ref _vendas, value);
            AtualizarEstados();
        }
    }

    private VendaSiteDto? _vendaSelecionada;
    public VendaSiteDto? VendaSelecionada
    {
        get => _vendaSelecionada;
        set => SetProperty(ref _vendaSelecionada, value);
    }

    // ---------------------------------------------------------------------------------------
    // Busca e paginação
    // ---------------------------------------------------------------------------------------

    private string _termoBusca = string.Empty;
    public string TermoBusca
    {
        get => _termoBusca;
        set
        {
            if (string.Equals(_termoBusca, value, StringComparison.Ordinal))
                return;

            _termoBusca = value ?? string.Empty;
            OnPropertyChanged();
            PaginaAtual = 1;
            _ = BuscarAsync();
        }
    }

    private int _paginaAtual = 1;
    public int PaginaAtual
    {
        get => _paginaAtual;
        set
        {
            if (_paginaAtual == value)
                return;

            _paginaAtual = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InfoPaginacao));
            OnPropertyChanged(nameof(PodePaginaAnterior));
            OnPropertyChanged(nameof(PodePaginaProxima));
        }
    }

    private int _totalPaginas;
    public int TotalPaginas { get => _totalPaginas; private set => SetProperty(ref _totalPaginas, value); }

    private int _totalItens;
    public int TotalItens { get => _totalItens; private set => SetProperty(ref _totalItens, value); }

    public string InfoPaginacao => TotalPaginas <= 0
        ? "Nenhuma venda do site"
        : $"Página {PaginaAtual} de {TotalPaginas} — {TotalItens} venda(s) do site";

    public bool PodePaginaAnterior => PaginaAtual > 1 && !Carregando;
    public bool PodePaginaProxima => PaginaAtual < TotalPaginas && !Carregando;

    // ---------------------------------------------------------------------------------------
    // Estados da lista: carregando, vazia, erro e integração indisponível
    // ---------------------------------------------------------------------------------------

    private SituacaoIntegracaoSite _situacaoIntegracao = SituacaoIntegracaoSite.Disponivel;

    private string _mensagemErroCarga = string.Empty;
    public string MensagemErroCarga
    {
        get => _mensagemErroCarga;
        private set
        {
            SetProperty(ref _mensagemErroCarga, value);
            AtualizarEstados();
        }
    }

    private bool _primeiraCargaConcluida;

    public bool TemErroCarga => !string.IsNullOrEmpty(MensagemErroCarga);

    public bool IntegracaoIndisponivel => _situacaoIntegracao != SituacaoIntegracaoSite.Disponivel;

    public string MensagemIntegracao => _situacaoIntegracao switch
    {
        SituacaoIntegracaoSite.NaoInstalada =>
            "A integração com o site ainda não foi instalada neste banco de dados. " +
            "Peça ao suporte para executar os scripts SQL do ImperialSync (imperialsync-integration-schema.sql e imperialsync-role.sql).",
        SituacaoIntegracaoSite.SemPermissao =>
            "O usuário que o sistema usa para acessar o banco não tem permissão para ler os dados da integração com o site. " +
            "Peça ao suporte para liberar a leitura do schema \"integration\".",
        _ => string.Empty
    };

    public bool TemVendas => Vendas.Count > 0;

    /// <summary>Lista vazia SEM explicação melhor: nada carregando, sem erro e integração no ar.
    /// (Antes da primeira carga a tela não mostra "nenhuma venda": ainda não se sabe.)</summary>
    public bool MostrarEstadoVazio =>
        _primeiraCargaConcluida && !Carregando && !TemErroCarga && !IntegracaoIndisponivel && !TemVendas;

    public bool MostrarEstadoErro => !Carregando && TemErroCarga;
    public bool MostrarIntegracaoIndisponivel => !Carregando && !TemErroCarga && IntegracaoIndisponivel;
    public bool MostrarCarregando => Carregando;

    // ---------------------------------------------------------------------------------------
    // Sincronização com o site
    // ---------------------------------------------------------------------------------------

    private bool _sincronizando;
    public bool Sincronizando
    {
        get => _sincronizando;
        private set
        {
            if (_sincronizando == value)
                return;

            _sincronizando = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TextoBotaoSincronizar));
            NotifyCanExecuteChanged();
        }
    }

    public string TextoBotaoSincronizar => Sincronizando ? "Sincronizando..." : "⇄  Sincronizar com o Site";

    private string _mensagemSincronizacao = string.Empty;
    public string MensagemSincronizacao
    {
        get => _mensagemSincronizacao;
        private set
        {
            SetProperty(ref _mensagemSincronizacao, value);
            OnPropertyChanged(nameof(TemMensagemSincronizacao));
        }
    }

    public bool TemMensagemSincronizacao => !string.IsNullOrEmpty(MensagemSincronizacao);

    private GravidadeSincronizacao _gravidadeSincronizacao;
    public GravidadeSincronizacao GravidadeSincronizacao
    {
        get => _gravidadeSincronizacao;
        private set => SetProperty(ref _gravidadeSincronizacao, value);
    }

    private string _rodapeSincronizacao = string.Empty;
    public string RodapeSincronizacao
    {
        get => _rodapeSincronizacao;
        private set
        {
            SetProperty(ref _rodapeSincronizacao, value);
            OnPropertyChanged(nameof(TemRodapeSincronizacao));
        }
    }

    public bool TemRodapeSincronizacao => !string.IsNullOrEmpty(RodapeSincronizacao);

    private string _detalhesSincronizacao = string.Empty;
    public string DetalhesSincronizacao
    {
        get => _detalhesSincronizacao;
        private set
        {
            SetProperty(ref _detalhesSincronizacao, value);
            OnPropertyChanged(nameof(TemDetalhesSincronizacao));
        }
    }

    public bool TemDetalhesSincronizacao => !string.IsNullOrEmpty(DetalhesSincronizacao);

    public AsyncRelayCommand CarregarCommand { get; }
    public AsyncRelayCommand SincronizarCommand { get; }
    public AsyncRelayCommand PaginaAnteriorCommand { get; }
    public AsyncRelayCommand PaginaProximaCommand { get; }

    public VendasSiteViewModel(IVendaSiteService vendaSiteService, ISincronizacaoSiteService sincronizacaoService)
    {
        _vendaSiteService = vendaSiteService;
        _sincronizacaoService = sincronizacaoService;

        CarregarCommand = new AsyncRelayCommand(CarregarAsync);
        SincronizarCommand = new AsyncRelayCommand(SincronizarAsync, () => !Sincronizando);
        PaginaAnteriorCommand = new AsyncRelayCommand(IrPaginaAnterior, () => PodePaginaAnterior);
        PaginaProximaCommand = new AsyncRelayCommand(IrPaginaProxima, () => PodePaginaProxima);

        // A tela é recriada a cada navegação, mas o serviço é único: se uma sincronização já está
        // rodando (iniciada antes de o operador sair e voltar), esta tela já nasce refletindo isso
        // e acompanha o fim dela.
        _sincronizando = _sincronizacaoService.EmExecucao;
        if (_sincronizando)
        {
            _mensagemSincronizacao = SincronizacaoSiteMensagens.Sincronizando;
            _gravidadeSincronizacao = GravidadeSincronizacao.Informacao;
        }

        OuvirServicoSemPrenderEstaTela();
    }

    /// <summary>
    /// O serviço vive o tempo do programa e esta tela não: um <c>+=</c> comum manteria a tela
    /// descartada viva (e ouvindo) para sempre. O ouvinte guarda só uma referência fraca para a
    /// tela e, quando ela já foi coletada, se remove do serviço no próximo aviso.
    /// </summary>
    private void OuvirServicoSemPrenderEstaTela()
    {
        var fraca = new WeakReference<VendasSiteViewModel>(this);
        var servico = _sincronizacaoService;
        EventHandler? ouvinte = null;
        ouvinte = (origem, argumentos) =>
        {
            if (fraca.TryGetTarget(out var tela))
                tela.AoAlterarEmExecucao(origem, argumentos);
            else
                servico.EmExecucaoAlterada -= ouvinte;
        };
        servico.EmExecucaoAlterada += ouvinte;
    }

    // ---------------------------------------------------------------------------------------
    // Carregamento
    // ---------------------------------------------------------------------------------------

    /// <summary>Recarrega a página atual (a primeira, ao abrir a tela).</summary>
    public async Task CarregarAsync()
    {
        PaginaAtual = Math.Max(1, PaginaAtual);
        await BuscarAsync();
    }

    private async Task BuscarAsync()
    {
        // Última busca vence: digitar rápido cancela as anteriores em vez de empilhá-las.
        _buscaCts?.Cancel();
        _buscaCts?.Dispose();
        _buscaCts = new CancellationTokenSource();
        var token = _buscaCts.Token;
        var semaforoAdquirido = false;

        try
        {
            await _buscaSemaforo.WaitAsync(token);
            semaforoAdquirido = true;
            if (token.IsCancellationRequested)
                return;

            Carregando = true;
            MensagemErroCarga = string.Empty;
            AtualizarEstados();

            var resultado = await _vendaSiteService.ObterPaginadoAsync(
                PaginaAtual,
                ItensPorPaginaPadrao,
                string.IsNullOrWhiteSpace(TermoBusca) ? null : TermoBusca.Trim(),
                token);

            if (token.IsCancellationRequested)
                return;

            _situacaoIntegracao = resultado.Situacao;
            TotalItens = resultado.Pagina.TotalItens;
            TotalPaginas = resultado.Pagina.TotalPaginas;

            // Se a página pedida deixou de existir (a busca encolheu o resultado), volta para a
            // última que existe em vez de mostrar uma grade vazia.
            if (TotalPaginas > 0 && PaginaAtual > TotalPaginas)
            {
                PaginaAtual = TotalPaginas;
                _ = BuscarAsync();
                return;
            }

            Vendas = new ObservableCollection<VendaSiteDto>(resultado.Pagina.Itens);
            VendaSelecionada = null;
            _primeiraCargaConcluida = true;
            OnPropertyChanged(nameof(InfoPaginacao));
            OnPropertyChanged(nameof(PodePaginaAnterior));
            OnPropertyChanged(nameof(PodePaginaProxima));
        }
        catch (OperationCanceledException)
        {
            // Substituída por uma busca mais nova: ela cuida de atualizar a tela.
        }
        catch (Exception ex)
        {
            Vendas = new ObservableCollection<VendaSiteDto>();
            TotalItens = 0;
            TotalPaginas = 0;
            _primeiraCargaConcluida = true;
            MensagemErroCarga = $"Não foi possível carregar as vendas do site: {ex.Message}";
        }
        finally
        {
            if (semaforoAdquirido)
            {
                Carregando = false;
                AtualizarEstados();
                OnPropertyChanged(nameof(PodePaginaAnterior));
                OnPropertyChanged(nameof(PodePaginaProxima));
                _buscaSemaforo.Release();
            }
        }
    }

    private async Task IrPaginaAnterior()
    {
        if (PaginaAtual <= 1)
            return;

        PaginaAtual--;
        await BuscarAsync();
    }

    private async Task IrPaginaProxima()
    {
        if (PaginaAtual >= TotalPaginas)
            return;

        PaginaAtual++;
        await BuscarAsync();
    }

    private void AtualizarEstados()
    {
        OnPropertyChanged(nameof(TemVendas));
        OnPropertyChanged(nameof(TemErroCarga));
        OnPropertyChanged(nameof(IntegracaoIndisponivel));
        OnPropertyChanged(nameof(MensagemIntegracao));
        OnPropertyChanged(nameof(MostrarEstadoVazio));
        OnPropertyChanged(nameof(MostrarEstadoErro));
        OnPropertyChanged(nameof(MostrarIntegracaoIndisponivel));
        OnPropertyChanged(nameof(MostrarCarregando));
    }

    // ---------------------------------------------------------------------------------------
    // Sincronização
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Roda o ImperialSync e, se ele chegou a executar, recarrega a lista (uma execução que falha
    /// no meio pode já ter criado vendas). Público e assíncrono — e não só o comando — para o
    /// teste conseguir esperar o fim; o botão chama este mesmo método.
    /// </summary>
    public async Task SincronizarAsync()
    {
        if (Sincronizando)
            return;

        _execucaoPropria = true;
        Sincronizando = true;
        DefinirMensagem(GravidadeSincronizacao.Informacao, SincronizacaoSiteMensagens.Sincronizando);

        ResultadoSincronizacaoSite resultado;
        try
        {
            resultado = await _sincronizacaoService.SincronizarAsync();
        }
        catch (Exception)
        {
            // O serviço não deve lançar; se lançar, a tela não pode ficar "sincronizando" para sempre.
            resultado = new ResultadoSincronizacaoSite
            {
                Status = StatusSincronizacaoSite.Falhou,
                Mensagem = SincronizacaoSiteMensagens.FalhaInesperada
            };
        }
        finally
        {
            _execucaoPropria = false;
        }

        // Normalmente falso; fica verdadeiro se OUTRA execução (de outra tela) está rodando.
        Sincronizando = _sincronizacaoService.EmExecucao;
        AplicarResultadoSincronizacao(resultado);

        if (resultado.ProcessoExecutado)
            await CarregarAsync();
    }

    private void AplicarResultadoSincronizacao(ResultadoSincronizacaoSite resultado)
    {
        var gravidade = resultado.Status switch
        {
            StatusSincronizacaoSite.Concluida => GravidadeSincronizacao.Sucesso,
            StatusSincronizacaoSite.ConcluidaComAtencao => GravidadeSincronizacao.Atencao,
            StatusSincronizacaoSite.JaEmExecucao => GravidadeSincronizacao.Atencao,
            StatusSincronizacaoSite.Cancelada => GravidadeSincronizacao.Atencao,
            _ => GravidadeSincronizacao.Erro
        };

        DefinirMensagem(gravidade, resultado.Mensagem);

        DetalhesSincronizacao = string.Join(
            Environment.NewLine,
            resultado.Detalhes.TakeLast(MaximoLinhasDetalhe));

        RodapeSincronizacao = resultado.ProcessoExecutado
            ? string.Create(PtBr, $"{DescreverCodigo(resultado)} · duração {resultado.Duracao.TotalSeconds:N1} s · {DateTime.Now:HH:mm:ss}")
            : string.Empty;
    }

    private static string DescreverCodigo(ResultadoSincronizacaoSite resultado)
        => resultado.CodigoSaida is { } codigo ? $"código de saída {codigo}" : "encerrada pelo sistema";

    private void DefinirMensagem(GravidadeSincronizacao gravidade, string mensagem)
    {
        GravidadeSincronizacao = gravidade;
        MensagemSincronizacao = mensagem;

        if (gravidade == GravidadeSincronizacao.Informacao)
        {
            DetalhesSincronizacao = string.Empty;
            RodapeSincronizacao = string.Empty;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Acompanhar uma sincronização iniciada em outra tela
    // ---------------------------------------------------------------------------------------

    // O serviço avisa de qualquer thread. PostarNaUi agenda sem esperar: bloquear a thread dele até a
    // interface responder travaria tudo se a interface estiver esperando o serviço terminar.
    private void AoAlterarEmExecucao(object? sender, EventArgs e)
        => UiDispatcher.PostarNaUi(AplicarEstadoDoServico);

    private void AplicarEstadoDoServico()
    {
        var rodando = _sincronizacaoService.EmExecucao;
        if (rodando == Sincronizando)
            return;

        Sincronizando = rodando;

        if (rodando)
        {
            if (!_execucaoPropria)
                DefinirMensagem(GravidadeSincronizacao.Informacao, SincronizacaoSiteMensagens.Sincronizando);
            return;
        }

        // Terminou. Se foi esta tela que iniciou, ela mesma mostra o resultado e recarrega.
        if (_execucaoPropria)
            return;

        if (_sincronizacaoService.UltimoResultado is { } ultimo)
            AplicarResultadoSincronizacao(ultimo);

        _ = CarregarAsync();
    }
}
