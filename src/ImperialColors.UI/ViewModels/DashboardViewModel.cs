using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;

namespace ImperialColors.UI.ViewModels;

public enum VisaoDashboard { Financeiro, Estoque, Vendas, Comissoes }

public class DashboardViewModel : BaseViewModel
{
    private readonly IDashboardService _dashboardService;

    // Cache em memória por visão — trocar de visão não recarrega se já foi buscada nesta
    // sessão de tela; só o botão "Atualizar" força um novo carregamento da visão atual.
    // A visão "mais vendidos" (dentro de Estoque) é a mais cara (cruza vendas de balcão +
    // externas do mês inteiro), então não faz sentido buscá-la toda vez que o Dashboard abre
    // se o operador só quer ver o financeiro.
    private bool _estoqueCarregado;
    private bool _vendasCarregado;
    private bool _comissoesCarregado;

    private VisaoDashboard _visaoAtual = VisaoDashboard.Financeiro;
    public VisaoDashboard VisaoAtual { get => _visaoAtual; private set => SetProperty(ref _visaoAtual, value); }

    public bool MostrarFinanceiro => VisaoAtual == VisaoDashboard.Financeiro;
    public bool MostrarEstoque => VisaoAtual == VisaoDashboard.Estoque;
    public bool MostrarVendas => VisaoAtual == VisaoDashboard.Vendas;
    public bool MostrarComissoes => VisaoAtual == VisaoDashboard.Comissoes;

    private decimal _totalVendasHoje;
    public decimal TotalVendasHoje { get => _totalVendasHoje; set => SetProperty(ref _totalVendasHoje, value); }

    private decimal _totalVendasMes;
    public decimal TotalVendasMes { get => _totalVendasMes; set => SetProperty(ref _totalVendasMes, value); }

    private int _produtosEstoqueCritico;
    public int ProdutosEstoqueCritico { get => _produtosEstoqueCritico; set => SetProperty(ref _produtosEstoqueCritico, value); }

    private int _produtosSemEstoque;
    public int ProdutosSemEstoque { get => _produtosSemEstoque; set => SetProperty(ref _produtosSemEstoque, value); }

    // --- Controle financeiro ---

    private decimal _lucroHoje;
    public decimal LucroHoje { get => _lucroHoje; set => SetProperty(ref _lucroHoje, value); }

    private decimal _lucroMes;
    public decimal LucroMes { get => _lucroMes; set => SetProperty(ref _lucroMes, value); }

    private decimal _custoHoje;
    public decimal CustoHoje { get => _custoHoje; set => SetProperty(ref _custoHoje, value); }

    private decimal _custoMes;
    public decimal CustoMes { get => _custoMes; set => SetProperty(ref _custoMes, value); }

    private decimal _margemLucroHoje;
    public decimal MargemLucroHoje { get => _margemLucroHoje; set => SetProperty(ref _margemLucroHoje, value); }

    private decimal _margemLucroMes;
    public decimal MargemLucroMes { get => _margemLucroMes; set => SetProperty(ref _margemLucroMes, value); }

    private decimal _ticketMedioMes;
    public decimal TicketMedioMes { get => _ticketMedioMes; set => SetProperty(ref _ticketMedioMes, value); }

    private int _quantidadeVendasMes;
    public int QuantidadeVendasMes { get => _quantidadeVendasMes; set => SetProperty(ref _quantidadeVendasMes, value); }

    private int _itensSemCustoCadastradoMes;
    public int ItensSemCustoCadastradoMes { get => _itensSemCustoCadastradoMes; set => SetProperty(ref _itensSemCustoCadastradoMes, value); }

    public bool ExisteItemSemCusto => ItensSemCustoCadastradoMes > 0;

    private List<LucroDiarioDto> _lucroUltimos7Dias = new();
    public List<LucroDiarioDto> LucroUltimos7Dias { get => _lucroUltimos7Dias; set => SetProperty(ref _lucroUltimos7Dias, value); }

    // --- Visão Estoque ---

    private List<ProdutoDto> _proximosDaValidade = new();
    public List<ProdutoDto> ProximosDaValidade { get => _proximosDaValidade; set => SetProperty(ref _proximosDaValidade, value); }

    private List<ProdutoDto> _poucaQuantidade = new();
    public List<ProdutoDto> PoucaQuantidade { get => _poucaQuantidade; set => SetProperty(ref _poucaQuantidade, value); }

    private List<ProdutoRankingDto> _maisVendidos = new();
    public List<ProdutoRankingDto> MaisVendidos { get => _maisVendidos; set => SetProperty(ref _maisVendidos, value); }

    // --- Visão Vendas ---

    private List<VendaDestaqueDto> _maioresVendas = new();
    public List<VendaDestaqueDto> MaioresVendas { get => _maioresVendas; set => SetProperty(ref _maioresVendas, value); }

    private PeriodoDashboard _periodoProdutosVendidos = PeriodoDashboard.Mes;
    public PeriodoDashboard PeriodoProdutosVendidos { get => _periodoProdutosVendidos; private set => SetProperty(ref _periodoProdutosVendidos, value); }

    public bool PeriodoEhMes => PeriodoProdutosVendidos == PeriodoDashboard.Mes;
    public bool PeriodoEhTotal => PeriodoProdutosVendidos == PeriodoDashboard.Total;

    private string SufixoPeriodo => PeriodoEhTotal ? "total" : "mês";
    public string TituloItensPorCategoria => $"Itens Vendidos por Categoria ({SufixoPeriodo})";
    public string TituloMaisVendidos => $"5 Mais Vendidos ({SufixoPeriodo})";
    public string MensagemSemItensVendidos => PeriodoEhTotal
        ? "Nenhum item vendido até agora."
        : "Nenhum item vendido neste mês.";

    public RelayCommand<PeriodoDashboard> TrocarPeriodoProdutosCommand { get; }

    private List<CategoriaItensVendidosDto> _itensPorCategoria = new();
    public List<CategoriaItensVendidosDto> ItensPorCategoria { get => _itensPorCategoria; set => SetProperty(ref _itensPorCategoria, value); }

    private List<ProdutoMaisVendidoDto> _produtosMaisVendidosMes = new();
    public List<ProdutoMaisVendidoDto> ProdutosMaisVendidosMes { get => _produtosMaisVendidosMes; set => SetProperty(ref _produtosMaisVendidosMes, value); }

    /// <summary>Sem item vendido no mês, os dois cards mostram uma mensagem em vez de uma
    /// área vazia que parece falha de carregamento.</summary>
    public bool SemItensVendidosNoMes => ItensPorCategoria.Count == 0;

    // --- Comissões de venda externa ---

    private decimal _comissoesAPagar;
    public decimal ComissoesAPagar { get => _comissoesAPagar; set => SetProperty(ref _comissoesAPagar, value); }

    private decimal _comissoesPagas;
    public decimal ComissoesPagas { get => _comissoesPagas; set => SetProperty(ref _comissoesPagas, value); }

    private decimal _comissoesDoMes;
    public decimal ComissoesDoMes { get => _comissoesDoMes; set => SetProperty(ref _comissoesDoMes, value); }

    private int _quantidadeComissoesAPagar;
    public int QuantidadeComissoesAPagar { get => _quantidadeComissoesAPagar; set => SetProperty(ref _quantidadeComissoesAPagar, value); }

    private List<ComissaoVendaExternaDto> _comissoesPendentes = new();
    public List<ComissaoVendaExternaDto> ComissoesPendentes { get => _comissoesPendentes; set => SetProperty(ref _comissoesPendentes, value); }

    public bool SemComissoesPendentes => ComissoesPendentes.Count == 0;

    public string DataHoje => DateTime.Now.ToString("dddd, dd 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("pt-BR"));

    public AsyncRelayCommand CarregarCommand { get; }
    public RelayCommand<VisaoDashboard> TrocarVisaoCommand { get; }

    public DashboardViewModel(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
        CarregarCommand = new AsyncRelayCommand(() => CarregarVisaoAtualAsync(forcar: true));
        // async void por trás de um Action<T> síncrono (mesmo padrão de risco/mitigação dos
        // "private async void Btn_Click" já usados em toda a UI do projeto) — o try/catch
        // dentro de cada CarregarXAsync garante que nenhuma exceção escapa do handler.
        TrocarVisaoCommand = new RelayCommand<VisaoDashboard>(async v => await TrocarVisaoAsync(v));
        TrocarPeriodoProdutosCommand = new RelayCommand<PeriodoDashboard>(async p => await TrocarPeriodoProdutosAsync(p));
    }

    /// <summary>Carregamento inicial ao navegar para o Dashboard — chamado direto pela
    /// navegação (fora do binding de comando), então precisa ficar público. Sempre carrega a
    /// visão Financeiro (visão padrão de um ViewModel recém-criado pela navegação).</summary>
    public Task CarregarDados() => CarregarVisaoAtualAsync(forcar: true);

    private async Task TrocarVisaoAsync(VisaoDashboard visao)
    {
        VisaoAtual = visao;
        OnPropertyChanged(nameof(MostrarFinanceiro));
        OnPropertyChanged(nameof(MostrarEstoque));
        OnPropertyChanged(nameof(MostrarVendas));
        OnPropertyChanged(nameof(MostrarComissoes));
        // Lazy: só busca se ainda não tem cache desta visão nesta sessão de tela.
        await CarregarVisaoAtualAsync(forcar: false);
    }

    /// <summary>Chamado pelo botão "Atualizar" (força reload, ignorando o cache) e ao trocar
    /// de visão (respeita o cache — ver <see cref="TrocarVisaoAsync"/>).</summary>
    private Task CarregarVisaoAtualAsync(bool forcar) => VisaoAtual switch
    {
        VisaoDashboard.Estoque => CarregarEstoqueAsync(forcar),
        VisaoDashboard.Vendas => CarregarVendasAsync(forcar),
        VisaoDashboard.Comissoes => CarregarComissoesAsync(forcar),
        _ => CarregarFinanceiroAsync()
    };

    private async Task CarregarFinanceiroAsync()
    {
        try
        {
            Carregando = true;
            var dados = await _dashboardService.ObterDadosDashboardAsync();
            TotalVendasHoje = dados.TotalVendasHoje;
            TotalVendasMes = dados.TotalVendasMes;
            ProdutosEstoqueCritico = dados.ProdutosEstoqueCritico;
            ProdutosSemEstoque = dados.ProdutosSemEstoque;

            LucroHoje = dados.LucroHoje;
            LucroMes = dados.LucroMes;
            CustoHoje = dados.CustoHoje;
            CustoMes = dados.CustoMes;
            MargemLucroHoje = dados.MargemLucroHoje;
            MargemLucroMes = dados.MargemLucroMes;
            TicketMedioMes = dados.TicketMedioMes;
            QuantidadeVendasMes = dados.QuantidadeVendasMes;
            ItensSemCustoCadastradoMes = dados.ItensSemCustoCadastradoMes;
            OnPropertyChanged(nameof(ExisteItemSemCusto));
            LucroUltimos7Dias = dados.LucroUltimos7Dias;
        }
        catch (Exception ex)
        {
            MostrarErro($"Erro ao carregar dashboard: {ex.Message}");
        }
        finally
        {
            Carregando = false;
        }
    }

    private async Task CarregarEstoqueAsync(bool forcar)
    {
        if (_estoqueCarregado && !forcar) return;

        try
        {
            Carregando = true;
            var dados = await _dashboardService.ObterVisaoEstoqueAsync();
            ProximosDaValidade = dados.ProximosDaValidade;
            PoucaQuantidade = dados.PoucaQuantidade;
            MaisVendidos = dados.MaisVendidos;
            _estoqueCarregado = true;
        }
        catch (Exception ex)
        {
            MostrarErro($"Erro ao carregar visão de estoque: {ex.Message}");
        }
        finally
        {
            Carregando = false;
        }
    }

    /// <summary>
    /// Painel de comissões: quanto a loja ainda deve a quem vendeu na rua, quanto já
    /// acertou, e quanto o mês gerou de comissão. Os pendentes vêm listados porque é a
    /// pergunta prática do lojista — "quem eu tenho que pagar?".
    /// </summary>
    private async Task CarregarComissoesAsync(bool forcar)
    {
        if (_comissoesCarregado && !forcar) return;

        try
        {
            Carregando = true;
            var resumo = await _dashboardService.ObterVisaoComissoesAsync();

            ComissoesAPagar = resumo.TotalAPagar;
            ComissoesPagas = resumo.TotalPago;
            ComissoesDoMes = resumo.TotalDoMes;
            QuantidadeComissoesAPagar = resumo.QuantidadeAPagar;
            ComissoesPendentes = resumo.Pendentes;
            OnPropertyChanged(nameof(SemComissoesPendentes));

            _comissoesCarregado = true;
        }
        catch (Exception ex)
        {
            MostrarErro($"Erro ao carregar comissões: {ex.Message}");
        }
        finally
        {
            Carregando = false;
        }
    }

    /// <summary>Trocar Mês/Total recarrega só os dois blocos de produtos — as "Maiores Vendas"
    /// continuam sendo do mês e não precisam ser buscadas de novo.</summary>
    private async Task TrocarPeriodoProdutosAsync(PeriodoDashboard periodo)
    {
        if (periodo == PeriodoProdutosVendidos)
        {
            // Clicar no botão já marcado desmarcaria o ToggleButton na tela; avisar a UI
            // devolve o estado que o ViewModel considera verdadeiro.
            NotificarPeriodo();
            return;
        }

        PeriodoProdutosVendidos = periodo;
        NotificarPeriodo();
        await CarregarProdutosVendidosAsync();
    }

    private void NotificarPeriodo()
    {
        OnPropertyChanged(nameof(PeriodoEhMes));
        OnPropertyChanged(nameof(PeriodoEhTotal));
        OnPropertyChanged(nameof(TituloItensPorCategoria));
        OnPropertyChanged(nameof(TituloMaisVendidos));
        OnPropertyChanged(nameof(MensagemSemItensVendidos));
    }

    private async Task CarregarProdutosVendidosAsync()
    {
        // Guarda o período pedido: se o operador alternar Mês/Total rápido, a resposta mais
        // lenta de uma consulta antiga não pode sobrescrever a tela do período atual.
        var periodoPedido = PeriodoProdutosVendidos;
        try
        {
            Carregando = true;
            var dados = await _dashboardService.ObterProdutosVendidosAsync(periodoPedido);
            if (periodoPedido != PeriodoProdutosVendidos)
                return;

            ItensPorCategoria = dados.ItensPorCategoria;
            ProdutosMaisVendidosMes = dados.ProdutosMaisVendidos;
            OnPropertyChanged(nameof(SemItensVendidosNoMes));
        }
        catch (Exception ex)
        {
            MostrarErro($"Erro ao carregar produtos vendidos: {ex.Message}");
        }
        finally
        {
            Carregando = false;
        }
    }

    private async Task CarregarVendasAsync(bool forcar)
    {
        if (_vendasCarregado && !forcar) return;

        try
        {
            Carregando = true;
            var dados = await _dashboardService.ObterVisaoVendasAsync();
            MaioresVendas = dados.MaioresVendas;
            await CarregarProdutosVendidosAsync();
            _vendasCarregado = true;
        }
        catch (Exception ex)
        {
            MostrarErro($"Erro ao carregar visão de vendas: {ex.Message}");
        }
        finally
        {
            Carregando = false;
        }
    }
}
