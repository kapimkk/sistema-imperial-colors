using ImperialColors.Application.DTOs;

using ImperialColors.Application.Interfaces;

using ImperialColors.Domain.Constants;
using ImperialColors.UI.Helpers;
using ImperialColors.UI.Services;
using System.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

using System.Collections.ObjectModel;



namespace ImperialColors.UI.ViewModels;



public class ProdutoViewModel : BaseViewModel

{

    public const int ItensPorPaginaPadrao = 50;



    private readonly IProdutoService _produtoService;

    private readonly IServiceScopeFactory _scopeFactory;

    private CancellationTokenSource? _buscaCts;

    private readonly SemaphoreSlim _buscaSemaforo = new(1, 1);



    private ObservableCollection<ProdutoDto> _produtos = new();

    public ObservableCollection<ProdutoDto> Produtos { get => _produtos; set => SetProperty(ref _produtos, value); }



    private ProdutoDto? _produtoSelecionado;

    public ProdutoDto? ProdutoSelecionado

    {

        get => _produtoSelecionado;

        set

        {

            SetProperty(ref _produtoSelecionado, value);

            OnPropertyChanged(nameof(TemSelecao));
            NotifyCanExecuteChanged();

        }

    }



    public bool TemSelecao => ProdutoSelecionado is not null;



    private string _termoBusca = string.Empty;

    public string TermoBusca
    {

        get => _termoBusca;

        set

        {

            if (!SetPropertyIfChanged(ref _termoBusca, value))

                return;



            PaginaAtual = 1;

            _ = BuscarAsync();

        }

    }

    private bool _apenasEmPromocao;

    public bool ApenasEmPromocao
    {
        get => _apenasEmPromocao;
        set
        {
            if (!SetPropertyIfChanged(ref _apenasEmPromocao, value))
                return;

            PaginaAtual = 1;
            _ = BuscarAsync();
        }
    }



    // ---------- Filtro por unidade de medida ----------

    private readonly IFiltroUnidadeEstoqueStore? _filtroUnidadeStore;

    // Ligado enquanto o "Limpar filtro" desmarca várias opções de uma vez: sem isso cada uma
    // dispararia uma busca e uma gravação, e a tela piscaria entre resultados intermediários.
    private bool _aplicandoFiltroUnidade;

    /// <summary>Todas as unidades de medida cadastráveis, marcadas ou não.</summary>
    public ObservableCollection<OpcaoUnidadeFiltro> OpcoesUnidade { get; } = new();

    /// <summary>Siglas marcadas. Vazio = sem filtro, lista inteira.</summary>
    public IReadOnlyList<string> UnidadesFiltradas
        => OpcoesUnidade.Where(o => o.Selecionada).Select(o => o.Sigla).ToList();

    public bool FiltroUnidadeAtivo => OpcoesUnidade.Any(o => o.Selecionada);

    /// <summary>Texto do botão: deixa à vista que há filtro ligado, porque ele persiste e
    /// ninguém pode esquecer por que metade do estoque sumiu da lista.</summary>
    public string TextoFiltroUnidade
    {
        get
        {
            var marcadas = UnidadesFiltradas;
            return marcadas.Count switch
            {
                0 => "Unidade",
                <= 3 => $"Unidade: {string.Join(", ", marcadas)}",
                _ => $"Unidade: {marcadas.Count} selecionadas"
            };
        }
    }

    /// <summary>"140 produtos cadastrados" não vale com filtro ligado: o número passa a ser o do
    /// que o filtro deixa passar, e o texto diz isso.</summary>
    public string ResumoTotalProdutos
    {
        get
        {
            if (!FiltroUnidadeAtivo)
                return $"{TotalProdutos} produtos cadastrados";

            var marcadas = UnidadesFiltradas;
            return marcadas.Count == 1
                ? $"{TotalProdutos} produtos na unidade {marcadas[0]}"
                : $"{TotalProdutos} produtos nas unidades {string.Join(", ", marcadas)}";
        }
    }

    private bool _filtroUnidadeAberto;

    public bool FiltroUnidadeAberto
    {
        get => _filtroUnidadeAberto;
        set => SetPropertyIfChanged(ref _filtroUnidadeAberto, value);
    }

    private string _avisoFiltroUnidade = string.Empty;

    /// <summary>Aviso quando a leitura de um código de barras achou o produto, mas ele é de
    /// uma unidade fora do filtro — sem isso a lista ficaria vazia e pareceria "não achou".</summary>
    public string AvisoFiltroUnidade
    {
        get => _avisoFiltroUnidade;
        private set
        {
            if (SetPropertyIfChanged(ref _avisoFiltroUnidade, value))
                OnPropertyChanged(nameof(TemAvisoFiltroUnidade));
        }
    }

    public bool TemAvisoFiltroUnidade => !string.IsNullOrEmpty(AvisoFiltroUnidade);

    public RelayCommand LimparFiltroUnidadeCommand { get; }

    private void OpcaoUnidade_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_aplicandoFiltroUnidade || e.PropertyName != nameof(OpcaoUnidadeFiltro.Selecionada))
            return;

        AplicarFiltroUnidade();
    }

    private void LimparFiltroUnidade()
    {
        _aplicandoFiltroUnidade = true;
        try
        {
            foreach (var opcao in OpcoesUnidade)
                opcao.Selecionada = false;
        }
        finally
        {
            _aplicandoFiltroUnidade = false;
        }

        FiltroUnidadeAberto = false;
        AplicarFiltroUnidade();
    }

    /// <summary>Filtro mudou: guarda, avisa a tela e refaz a busca desde a primeira página (a
    /// página 3 de "todas" não tem relação com a página 3 de "só BD").</summary>
    private void AplicarFiltroUnidade()
    {
        OnPropertyChanged(nameof(UnidadesFiltradas));
        OnPropertyChanged(nameof(FiltroUnidadeAtivo));
        OnPropertyChanged(nameof(TextoFiltroUnidade));
        OnPropertyChanged(nameof(ResumoTotalProdutos));
        AvisoFiltroUnidade = string.Empty;

        _filtroUnidadeStore?.Salvar(UnidadesFiltradas);
        NotifyCanExecuteChanged();

        PaginaAtual = 1;
        _ = BuscarAsync();
    }

    private int _paginaAtual = 1;

    public int PaginaAtual

    {

        get => _paginaAtual;

        set

        {

            if (!SetPropertyIfChanged(ref _paginaAtual, value))

                return;



            OnPropertyChanged(nameof(InfoPaginacao));

            OnPropertyChanged(nameof(PodePaginaAnterior));

            OnPropertyChanged(nameof(PodePaginaProxima));

        }

    }



    private int _totalPaginas;

    public int TotalPaginas { get => _totalPaginas; set => SetProperty(ref _totalPaginas, value); }



    private int _totalProdutos;

    public int TotalProdutos { get => _totalProdutos; set => SetProperty(ref _totalProdutos, value); }



    public string InfoPaginacao => TotalPaginas <= 0

        ? "Nenhum produto"

        : $"Página {PaginaAtual} de {TotalPaginas}";



    public bool PodePaginaAnterior => PaginaAtual > 1 && !Carregando;

    public bool PodePaginaProxima => PaginaAtual < TotalPaginas && !Carregando;



    public AsyncRelayCommand CarregarCommand { get; }

    public AsyncRelayCommand NovoProdutoCommand { get; }

    public AsyncRelayCommand EditarProdutoCommand { get; }

    public AsyncRelayCommand ExcluirProdutoCommand { get; }

    public AsyncRelayCommand MovimentacaoCommand { get; }

    public AsyncRelayCommand PaginaAnteriorCommand { get; }

    public AsyncRelayCommand PaginaProximaCommand { get; }

    public AsyncRelayCommand AtualizarCommand { get; }



    public ProdutoViewModel(
        IProdutoService produtoService,
        IServiceScopeFactory scopeFactory,
        IFiltroUnidadeEstoqueStore? filtroUnidadeStore = null)

    {

        _produtoService = produtoService;

        _scopeFactory = scopeFactory;
        _filtroUnidadeStore = filtroUnidadeStore;

        // O filtro de unidade volta como o operador deixou: ele só sai quando ele clica em
        // "Limpar filtro". O ViewModel é recriado a cada visita à tela, então o estado vem do
        // arquivo, e não do que estava em memória.
        var salvas = _filtroUnidadeStore?.Carregar() ?? [];
        foreach (var sigla in UnidadesMedida.Todas)
        {
            var opcao = new OpcaoUnidadeFiltro(sigla, salvas.Contains(sigla));
            opcao.PropertyChanged += OpcaoUnidade_PropertyChanged;
            OpcoesUnidade.Add(opcao);
        }
        LimparFiltroUnidadeCommand = new RelayCommand(LimparFiltroUnidade, () => FiltroUnidadeAtivo);



        CarregarCommand = new AsyncRelayCommand(CarregarAsync);

        AtualizarCommand = new AsyncRelayCommand(CarregarAsync);

        NovoProdutoCommand = new AsyncRelayCommand(AbrirNovoProduto);

        EditarProdutoCommand = new AsyncRelayCommand(AbrirEditarProduto, () => TemSelecao && !Carregando);

        ExcluirProdutoCommand = new AsyncRelayCommand(ExcluirProduto, () => TemSelecao && !Carregando);

        MovimentacaoCommand = new AsyncRelayCommand(AbrirMovimentacao, () => TemSelecao && !Carregando);

        PaginaAnteriorCommand = new AsyncRelayCommand(IrPaginaAnterior, () => PodePaginaAnterior);

        PaginaProximaCommand = new AsyncRelayCommand(IrPaginaProxima, () => PodePaginaProxima);

    }



    public async Task CarregarAsync()

    {

        PaginaAtual = Math.Max(1, PaginaAtual);

        await BuscarAsync();

    }



    private async Task BuscarAsync()

    {

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

            try
            {
                await Task.Delay(300, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            UiDispatcher.ExecutarNaUi(() => { AvisoFiltroUnidade = string.Empty; Carregando = true; });

            var resultado = await _produtoService.ObterPaginadoAsync(
                PaginaAtual,
                ItensPorPaginaPadrao,
                string.IsNullOrWhiteSpace(TermoBusca) ? null : TermoBusca.Trim(),
                ApenasEmPromocao,
                FiltroUnidadeAtivo ? UnidadesFiltradas : null,
                token).ConfigureAwait(false);

            if (token.IsCancellationRequested)
                return;

            UiDispatcher.ExecutarNaUi(() =>
            {
                Produtos = new ObservableCollection<ProdutoDto>(resultado.Itens);
                TotalProdutos = resultado.TotalItens;
                TotalPaginas = resultado.TotalPaginas;

                if (TotalPaginas > 0 && PaginaAtual > TotalPaginas)
                {
                    PaginaAtual = TotalPaginas;
                    _ = BuscarAsync();
                    return;
                }

                ProdutoSelecionado = null;
                OnPropertyChanged(nameof(ResumoTotalProdutos));
                OnPropertyChanged(nameof(InfoPaginacao));
                OnPropertyChanged(nameof(PodePaginaAnterior));
                OnPropertyChanged(nameof(PodePaginaProxima));
            });
        }
        catch (OperationCanceledException)
        {
            // Busca substituída por nova digitação ou navegação.
        }
        catch (Exception ex)
        {
            UiDispatcher.ExecutarNaUi(() =>
                MostrarErro($"Erro na busca: {ex.Message}"));
        }
        finally
        {
            if (semaforoAdquirido)
            {
                UiDispatcher.ExecutarNaUi(() => Carregando = false);
                _buscaSemaforo.Release();
            }
        }

    }



    private async Task IrPaginaAnterior()

    {

        if (PaginaAtual <= 1) return;

        PaginaAtual--;

        await BuscarAsync();

    }



    private async Task IrPaginaProxima()

    {

        if (PaginaAtual >= TotalPaginas) return;

        PaginaAtual++;

        await BuscarAsync();

    }



    private async Task AbrirNovoProduto()

    {

        try

        {

            using var escopo = _scopeFactory.CreateScope();

            var form = escopo.ServiceProvider.GetRequiredService<Views.ProdutoFormView>();

            form.InicializarNovo();

            if (ModalWindowHelper.ExibirDialogo(form) == true)

                await CarregarAsync();

        }

        catch (Exception ex)

        {

            MostrarErro($"Erro ao abrir cadastro: {ex.Message}");

        }

    }



    private async Task AbrirEditarProduto()
    {
        if (!ValidarSelecao(ProdutoSelecionado, "produto"))
            return;

        var produtoId = ProdutoSelecionado!.Id;

        try
        {
            var produto = await _produtoService.ObterPorIdAsync(produtoId);
            if (produto is null)
            {
                MostrarErro("Produto não encontrado ou foi removido.");
                return;
            }

            using var escopo = _scopeFactory.CreateScope();
            var form = escopo.ServiceProvider.GetRequiredService<Views.ProdutoFormView>();
            form.InicializarEdicao(produto);

            if (ModalWindowHelper.ExibirDialogo(form) == true)
                await CarregarAsync();
        }
        catch (Exception ex)
        {
            MostrarErro($"Erro ao abrir edição: {ex.Message}");
        }
    }



    public void ExecutarEdicaoSeSelecionado()
    {
        if (ValidarSelecao(ProdutoSelecionado, "produto"))
            EditarProdutoCommand.Execute(null);
    }

    private async Task ExcluirProduto()
    {
        if (!ValidarSelecao(ProdutoSelecionado, "produto"))
            return;

        var produto = ProdutoSelecionado!;

        if (!ConfirmarAcao($"Deseja excluir permanentemente o produto '{produto.Nome}'?\n\nEsta ação remove o registro do banco e não pode ser desfeita.")) return;



        try

        {

            Carregando = true;

            await _produtoService.RemoverAsync(produto.Id);

            MostrarSucesso("Produto excluído com sucesso!");

            await CarregarAsync();

        }

        catch (Exception ex)

        {

            MostrarErro($"Erro ao excluir: {ex.Message}");

        }

        finally

        {

            Carregando = false;

        }

    }



    private async Task AbrirMovimentacao()
    {
        if (!ValidarSelecao(ProdutoSelecionado, "produto"))
            return;

        var produtoId = ProdutoSelecionado!.Id;

        try
        {
            var produto = await _produtoService.ObterPorIdAsync(produtoId);
            if (produto is null)
            {
                MostrarErro("Produto não encontrado ou foi removido.");
                return;
            }

            using var escopo = _scopeFactory.CreateScope();
            var form = escopo.ServiceProvider.GetRequiredService<Views.MovimentacaoEstoqueView>();
            form.InicializarProduto(produto);

            if (ModalWindowHelper.ExibirDialogo(form) == true)

                await CarregarAsync();

        }

        catch (Exception ex)

        {

            MostrarErro($"Erro ao abrir movimentação: {ex.Message}");

        }

    }



    public async Task<bool> ProcessarLeituraCodigoBarrasAsync(string codigo)

    {

        var produto = await _produtoService.ObterPorCodigoBarrasAsync(codigo)

                      ?? await _produtoService.ObterPorCodigoInternoAsync(codigo);



        if (produto is null)

            return false;



        // Achou, mas o produto é de uma unidade fora do filtro ligado: a lista ficaria vazia e
        // pareceria "não encontrado". Em vez disso, o operador é avisado do porquê.
        var unidadeDoProduto = UnidadesMedida.Normalizar(produto.Unidade);
        if (FiltroUnidadeAtivo && !UnidadesFiltradas.Contains(unidadeDoProduto))
        {
            AvisoFiltroUnidade =
                $"\"{produto.Nome}\" é da unidade {unidadeDoProduto}, fora do filtro ativo " +
                $"({string.Join(", ", UnidadesFiltradas)}). Clique em Limpar filtro para vê-lo.";
            return true;
        }

        TermoBusca = produto.CodigoBarras ?? produto.CodigoInterno ?? produto.Nome;

        ProdutoSelecionado = produto;

        return true;

    }



    private bool SetPropertyIfChanged<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")

    {

        if (EqualityComparer<T>.Default.Equals(field, value))

            return false;



        field = value;

        OnPropertyChanged(propertyName);

        return true;

    }

}


