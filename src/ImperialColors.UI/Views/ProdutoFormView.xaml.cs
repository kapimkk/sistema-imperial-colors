using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Application.Validation;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Exceptions;
using ImperialColors.UI.Helpers;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using ImperialColors.Infrastructure.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ImperialColors.UI.Views;

public partial class ProdutoFormView : Window
{
    private readonly IProdutoService _produtoService;
    private readonly ICategoriaService _categoriaService;
    private readonly IMarcaService _marcaService;
    private readonly IFornecedorService _fornecedorService;
    private readonly IConfiguracaoFiscalService _configuracaoFiscal;
    private readonly INcmService _ncmService;
    private readonly IImagemProdutoStorage _imagens;
    private readonly IProdutoFormDialogs _dialogs;
    private string? _imagemReferencia;
    private string? _imagemSelecionada;
    private bool _removerImagem;
    private int? _produtoId;
    private decimal? _quantidadeEstoqueCarregadaNaEdicao;
    private bool _codigoDefinidoManualmente;
    private bool _codigoDesbloqueadoManualmente;
    private string? _ultimoCodigoGeradoAutomaticamente;
    private bool _ignorarAlteracaoCodigoInterno;
    private bool _modoCustoTotal;
    private bool _suprimirAtualizacaoCusto;
    private bool _suprimirEventosUi;
    private bool _preenchendoNcmProgramaticamente;
    private DispatcherTimer? _ncmDebounceTimer;
    private CancellationTokenSource? _ncmCts;
    private RegimeTributario _regimeAtual = RegimeTributario.SimplesNacional;

    public ProdutoFormView(
        IProdutoService produtoService,
        ICategoriaService categoriaService,
        IMarcaService marcaService,
        IFornecedorService fornecedorService,
        IConfiguracaoFiscalService configuracaoFiscal,
        INcmService ncmService,
        IImagemProdutoStorage? imagens = null, IProdutoFormDialogs? dialogs = null)
    {
        InitializeComponent();
        ModalWindowHelper.AplicarEstiloModerno(this);
        MaxHeight = SystemParameters.WorkArea.Height;
        _produtoService = produtoService;
        _categoriaService = categoriaService;
        _marcaService = marcaService;
        _fornecedorService = fornecedorService;
        _configuracaoFiscal = configuracaoFiscal;
        _ncmService = ncmService;
        _imagens = imagens ?? new ImagemProdutoStorage();
        _dialogs = dialogs ?? new ProdutoFormDialogs();

        SelecionarUnidadePadrao();
        Loaded += OnLoadedInicial;
    }

    private async void OnLoadedInicial(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedInicial;
        await CarregarComboBoxesAsync(_categoriaPendente, _marcaPendente, _fornecedorPendente);
        await CarregarRegimeTributarioAsync();
    }

    private async Task CarregarRegimeTributarioAsync()
    {
        var regime = await _configuracaoFiscal.ObterRegimeAsync();
        AplicarRegimeTributario(regime);
    }

    private void AplicarRegimeTributario(RegimeTributario regime)
    {
        _regimeAtual = regime;

        var usaCsosn = regime == RegimeTributario.SimplesNacional;
        PainelCstIcms.Visibility = usaCsosn ? Visibility.Collapsed : Visibility.Visible;
        PainelCsosnIcms.Visibility = usaCsosn ? Visibility.Visible : Visibility.Collapsed;

        TxtRegimeInfo.Text = usaCsosn
            ? "Regime da empresa: Simples Nacional — informe o CSOSN do ICMS."
            : $"Regime da empresa: {DescricaoRegime(regime)} — informe o CST do ICMS.";
    }

    private static string DescricaoRegime(RegimeTributario regime) => regime switch
    {
        RegimeTributario.LucroPresumido => "Lucro Presumido",
        RegimeTributario.LucroReal => "Lucro Real",
        _ => "Simples Nacional"
    };

    private void SelecionarUnidadePadrao()
    {
        foreach (ComboBoxItem item in CmbUnidade.Items)
        {
            if (item.Content?.ToString() == "UN")
            {
                CmbUnidade.SelectedItem = item;
                break;
            }
        }
    }

    // Categoria, marca e fornecedor do produto aberto para edição. Ficam guardados porque o
    // formulário é preenchido ANTES de a janela abrir, e a lista de opções só é carregada
    // depois (evento Loaded) — quem carrega por último precisa saber o que selecionar.
    //
    // Antes, InicializarEdicao disparava um carregamento COM os ids e o Loaded disparava outro
    // SEM nenhum: o que terminasse por último vencia, e o do Loaded (sem ids) cai no primeiro
    // item da lista. Resultado: produto da marca "Paraná Color" abria como "ATLAS", e o mesmo
    // com categoria. Nada era gravado errado — só a tela mentia — mas salvar assim trocaria a
    // marca do produto sem ninguém perceber.
    private int? _categoriaPendente;
    private int? _marcaPendente;
    private int? _fornecedorPendente;

    // Cada carregamento recebe um número; só o mais recente aplica o resultado. Cobre também o
    // caso de dois carregamentos em voo, em que a resposta mais lenta (e mais velha) chegaria
    // depois e sobrescreveria a seleção certa.
    private int _geracaoCarregamentoCombos;

    private async Task CarregarComboBoxesAsync(int? categoriaId = null, int? marcaId = null, int? fornecedorId = null)
    {
        var geracao = ++_geracaoCarregamentoCombos;

        var categorias = (await _categoriaService.ObterTodosAsync()).ToList();
        var marcas = (await _marcaService.ObterTodosAsync()).ToList();
        var fornecedores = (await _fornecedorService.ObterTodosAsync()).ToList();

        if (geracao != _geracaoCarregamentoCombos)
            return;

        CmbCategoria.ItemsSource = categorias;
        CmbMarca.ItemsSource = marcas;
        CmbFornecedor.ItemsSource = fornecedores;

        if (categoriaId.HasValue && categoriaId > 0)
            CmbCategoria.SelectedValue = categoriaId;
        else if (categorias.Count > 0)
            CmbCategoria.SelectedIndex = 0;

        if (marcaId.HasValue && marcaId > 0)
            CmbMarca.SelectedValue = marcaId;
        else if (marcas.Count > 0)
            CmbMarca.SelectedIndex = 0;

        CmbFornecedor.SelectedValue = fornecedorId is > 0 ? fornecedorId : null;
    }

    public void InicializarNovo()
    {
        TxtTitulo.Text = "Novo Produto";
        _produtoId = null;
        _categoriaPendente = null;
        _marcaPendente = null;
        _fornecedorPendente = null;
        _quantidadeEstoqueCarregadaNaEdicao = null;
        _codigoDefinidoManualmente = false;
        _codigoDesbloqueadoManualmente = false;
        _ultimoCodigoGeradoAutomaticamente = null;
        LimparErroValidacao();
        ChkCustoTotal.IsChecked = false;
        TxtCustoTotal.Text = string.Empty;
        PainelCustoTotal.Visibility = Visibility.Collapsed;
        TxtCusto.IsReadOnly = false;
        TxtCusto.Text = string.Empty;
        TxtPrecoVenda.Text = FormattingHelper.FormatarMoedaEntrada(0m);
        TxtNome.Text = string.Empty;
        TxtCodigoBarras.Text = string.Empty;
        TxtTamanhoEmbalagem.Text = string.Empty;
        TxtPesoKg.Text = string.Empty;
        TxtAlturaCm.Text = string.Empty;
        TxtLarguraCm.Text = string.Empty;
        TxtComprimentoCm.Text = string.Empty;
        _imagemReferencia = null;
        _imagemSelecionada = null;
        _removerImagem = false;
        AtualizarImagem();
        TxtRegraFrete.Text = "Obrigatórios para novos produtos. Peso em kg (até 3 casas); dimensões em cm (até 2).";
        TxtObservacoes.Text = string.Empty;
        DpValidade.SelectedDate = null;
        ChkPromocaoAtiva.IsChecked = false;
        TxtPrecoPromocional.Text = string.Empty;
        PainelPrecoPromocional.Visibility = Visibility.Collapsed;
        DefinirCodigoInternoSemMarcarManual(string.Empty);
        AplicarEstadoCampoCodigo();
        LimparCamposTributacao();
        ExpTributacao.IsExpanded = false;
    }

    public void InicializarEdicao(ProdutoDto produto)
    {
        ArgumentNullException.ThrowIfNull(produto);

        _suprimirEventosUi = true;
        try
        {
            TxtTitulo.Text = "Editar Produto";
            _produtoId = produto.Id;
            // Guardado para calcular o delta real pretendido no save (ver
            // AtualizarProdutoDto.QuantidadeEstoqueOriginal) — não o valor que o campo
            // tiver na hora de salvar, que pode já ter sido editado pelo usuário.
            _quantidadeEstoqueCarregadaNaEdicao = produto.QuantidadeEstoque;
            _codigoDefinidoManualmente = true;
            _codigoDesbloqueadoManualmente = false;
            LimparErroValidacao();
            ChkCustoTotal.IsChecked = false;
            TxtCustoTotal.Text = string.Empty;
            PainelCustoTotal.Visibility = Visibility.Collapsed;
            TxtCusto.IsReadOnly = false;
            DefinirCodigoInternoSemMarcarManual(produto.CodigoInterno ?? string.Empty);
            TxtCodigoBarras.Text = produto.CodigoBarras ?? string.Empty;
            TxtNome.Text = produto.Nome ?? string.Empty;
            // Exato: com "N1", 1,25 aparecia como 1,3 e salvar sem mexer no campo ajustava o estoque para 1,3.
            TxtQuantidade.Text = FormattingHelper.FormatarQuantidade(produto.QuantidadeEstoque);
            TxtEstoqueMinimo.Text = FormattingHelper.FormatarQuantidade(produto.EstoqueMinimo);
            TxtCusto.Text = FormattingHelper.FormatarMoedaEntrada(produto.Custo);
            TxtPrecoVenda.Text = FormattingHelper.FormatarMoedaEntrada(produto.PrecoVenda);
            TxtObservacoes.Text = produto.Observacoes ?? string.Empty;
            DpValidade.SelectedDate = produto.DataValidade;
            ChkPromocaoAtiva.IsChecked = produto.PromocaoAtiva;
            TxtPrecoPromocional.Text = FormattingHelper.FormatarMoedaEntrada(produto.PrecoPromocional);
            PainelPrecoPromocional.Visibility = produto.PromocaoAtiva ? Visibility.Visible : Visibility.Collapsed;

            foreach (ComboBoxItem item in CmbUnidade.Items)
            {
                if (item.Content?.ToString() == produto.Unidade)
                {
                    CmbUnidade.SelectedItem = item;
                    break;
                }
            }

            TxtTamanhoEmbalagem.Text = produto.TamanhoEmbalagem ?? string.Empty;
            TxtPesoKg.Text = PesoProdutoHelper.EmQuilos(produto.PesoGramas)?.ToString("0.000", new CultureInfo("pt-BR")) ?? string.Empty;
            TxtAlturaCm.Text = produto.AlturaCm?.ToString("0.00", new CultureInfo("pt-BR")) ?? string.Empty;
            TxtLarguraCm.Text = produto.LarguraCm?.ToString("0.00", new CultureInfo("pt-BR")) ?? string.Empty;
            TxtComprimentoCm.Text = produto.ComprimentoCm?.ToString("0.00", new CultureInfo("pt-BR")) ?? string.Empty;
            _imagemReferencia = produto.ImagemProdutoPath;
            _imagemSelecionada = null;
            _removerImagem = false;
            AtualizarImagem();
            TxtRegraFrete.Text = "Produto existente: dados ainda não informados podem ser mantidos pendentes. Preencha antes do envio por transportadora.";
            AtualizarPesoEquivalente();
        }
        finally
        {
            _suprimirEventosUi = false;
        }

        AplicarEstadoCampoCodigo();
        _categoriaPendente = produto.CategoriaId;
        _marcaPendente = produto.MarcaId;
        _fornecedorPendente = produto.FornecedorId;

        // Janela ainda não aberta: o Loaded carrega as listas com a seleção acima. Já aberta
        // (formulário reaproveitado): carrega agora, senão as listas ficariam com a escolha antiga.
        if (IsLoaded)
            _ = CarregarComboBoxesAsync(_categoriaPendente, _marcaPendente, _fornecedorPendente);

        _ = CarregarTributacaoAsync(produto.Id);
    }

    private async Task CarregarTributacaoAsync(int produtoId)
    {
        LimparCamposTributacao();

        TributacaoProdutoDto tributacao;
        try
        {
            tributacao = await _produtoService.ObterTributacaoAsync(produtoId);
        }
        catch
        {
            // Falha ao carregar tributação não deve impedir a edição do produto —
            // o usuário ainda pode salvar os dados comerciais normalmente.
            return;
        }

        if (tributacao is null || !tributacao.Preenchida)
            return;

        PreencherCamposTributacao(tributacao);
        ExpTributacao.IsExpanded = true;
    }

    private void PreencherCamposTributacao(TributacaoProdutoDto tributacao)
    {
        DefinirNcmSemAutocomplete(tributacao.Ncm ?? string.Empty);
        TxtCest.Text = tributacao.Cest ?? string.Empty;
        DefinirOrigemSelecionada(tributacao.Origem);
        TxtCstIcms.Text = tributacao.CstIcms ?? string.Empty;
        TxtCsosnIcms.Text = tributacao.CsosnIcms ?? string.Empty;
        TxtAliquotaIcms.Text = FormatarPercentual(tributacao.AliquotaIcms);
        TxtAliquotaIcmsSt.Text = FormatarPercentual(tributacao.AliquotaIcmsSt);
        TxtMva.Text = FormatarPercentual(tributacao.Mva);
        TxtReducaoBaseCalculo.Text = FormatarPercentual(tributacao.ReducaoBaseCalculo);
        TxtAliquotaIcmsStRetido.Text = FormatarPercentual(tributacao.AliquotaIcmsStRetido);
        TxtCstPis.Text = tributacao.CstPis ?? string.Empty;
        TxtAliquotaPis.Text = FormatarPercentual(tributacao.AliquotaPis);
        TxtCstCofins.Text = tributacao.CstCofins ?? string.Empty;
        TxtAliquotaCofins.Text = FormatarPercentual(tributacao.AliquotaCofins);
        TxtCstIpi.Text = tributacao.CstIpi ?? string.Empty;
        TxtCodigoEnquadramentoIpi.Text = tributacao.CodigoEnquadramentoIpi ?? string.Empty;
        TxtAliquotaIpi.Text = FormatarPercentual(tributacao.AliquotaIpi);
        TxtValorIpiFixo.Text = FormatarPercentual(tributacao.ValorIpiFixo);
        TxtExTipi.Text = tributacao.ExTipi ?? string.Empty;
        TxtUnidadeTributavel.Text = tributacao.UnidadeTributavel ?? string.Empty;
        TxtFatorConversao.Text = FormatarPercentual(tributacao.FatorConversao);
        TxtGtinTributavel.Text = tributacao.GtinTributavel ?? string.Empty;
        TxtCfopDentroEstado.Text = tributacao.CfopDentroEstado ?? string.Empty;
        TxtCfopForaEstado.Text = tributacao.CfopForaEstado ?? string.Empty;
        TxtCstIbsCbs.Text = tributacao.CstIbsCbs ?? string.Empty;
        TxtCClassTrib.Text = tributacao.CClassTrib ?? string.Empty;
        TxtCstIS.Text = tributacao.CstIS ?? string.Empty;
        TxtCClassTribIS.Text = tributacao.CClassTribIS ?? string.Empty;
        TxtAliquotaIS.Text = FormatarPercentual(tributacao.AliquotaIS);
        TxtAliquotaIbsMunicipioDiferimento.Text = FormatarPercentual(tributacao.AliquotaIbsMunicipioDiferimento);
        TxtAliquotaIbsMunicipioReducao.Text = FormatarPercentual(tributacao.AliquotaIbsMunicipioReducao);
    }

    /// <summary>
    /// Ao escolher a categoria de um produto NOVO (ainda sem tributação preenchida na
    /// tela), pré-preenche a seção fiscal com o padrão salvo para essa categoria, se
    /// existir. O usuário pode revisar e sobrescrever qualquer campo antes de salvar.
    /// </summary>
    private async void CmbCategoria_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suprimirEventosUi || _produtoId.HasValue || ExpTributacao.IsExpanded)
            return;

        var categoriaId = ObterIdSelecionado(CmbCategoria);
        if (categoriaId is not > 0)
            return;

        TributacaoCategoriaDto padrao;
        try
        {
            padrao = await _categoriaService.ObterTributacaoPadraoAsync(categoriaId.Value);
        }
        catch
        {
            return;
        }

        // Reconfere após o await: usuário pode ter aberto a seção manualmente enquanto
        // a consulta ao padrão da categoria estava em andamento.
        if (padrao is null || !padrao.Preenchida || _produtoId.HasValue || ExpTributacao.IsExpanded)
            return;

        PreencherCamposTributacao(padrao.ParaProdutoDto(produtoId: 0));
        TxtOrigemPadraoCategoria.Visibility = Visibility.Visible;
        ExpTributacao.IsExpanded = true;
    }

    private async void BtnSalvarPadraoCategoria_Click(object sender, RoutedEventArgs e)
    {
        var categoriaId = ObterIdSelecionado(CmbCategoria);
        if (categoriaId is not > 0)
        {
            MessageBox.Show("Selecione uma categoria antes de definir o padrão fiscal.", "Tributação",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tributacaoDto = MontarDtoTributacaoOuNulo();
        if (tributacaoDto is null)
        {
            MessageBox.Show("Preencha ao menos um campo fiscal antes de definir o padrão da categoria.",
                "Tributação", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            TributacaoProdutoValidator.Validar(tributacaoDto, _regimeAtual);

            var categoriaNome = (CmbCategoria.SelectedItem as CategoriaDto)?.Nome ?? "esta categoria";
            var confirmar = MessageBox.Show(
                $"Os campos fiscais preenchidos acima vão virar o padrão para novos produtos de \"{categoriaNome}\". " +
                "Produtos já cadastrados não são alterados. Continuar?",
                "Definir padrão fiscal da categoria", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmar != MessageBoxResult.Yes)
                return;

            var dtoCategoria = TributacaoCategoriaDto.DoProdutoDto(categoriaId.Value, tributacaoDto);
            await _categoriaService.SalvarTributacaoPadraoAsync(categoriaId.Value, dtoCategoria);

            MessageBox.Show("Padrão fiscal da categoria salvo.", "Tributação",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (DomainException ex)
        {
            MessageBox.Show(ex.Message, "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ExceptionMessageHelper.ObterMensagemAmigavel(ex), "Erro",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LimparCamposTributacao()
    {
        DefinirNcmSemAutocomplete(string.Empty);
        TxtCest.Text = string.Empty;
        CmbOrigem.SelectedItem = null;
        TxtCstIcms.Text = string.Empty;
        TxtCsosnIcms.Text = string.Empty;
        TxtAliquotaIcms.Text = string.Empty;
        TxtAliquotaIcmsSt.Text = string.Empty;
        TxtMva.Text = string.Empty;
        TxtReducaoBaseCalculo.Text = string.Empty;
        TxtAliquotaIcmsStRetido.Text = string.Empty;
        TxtCstPis.Text = string.Empty;
        TxtAliquotaPis.Text = string.Empty;
        TxtCstCofins.Text = string.Empty;
        TxtAliquotaCofins.Text = string.Empty;
        TxtCstIpi.Text = string.Empty;
        TxtCodigoEnquadramentoIpi.Text = string.Empty;
        TxtAliquotaIpi.Text = string.Empty;
        TxtValorIpiFixo.Text = string.Empty;
        TxtExTipi.Text = string.Empty;
        TxtUnidadeTributavel.Text = string.Empty;
        TxtFatorConversao.Text = string.Empty;
        TxtGtinTributavel.Text = string.Empty;
        TxtCfopDentroEstado.Text = string.Empty;
        TxtCfopForaEstado.Text = string.Empty;
        TxtCstIbsCbs.Text = string.Empty;
        TxtCClassTrib.Text = string.Empty;
        TxtCstIS.Text = string.Empty;
        TxtCClassTribIS.Text = string.Empty;
        TxtAliquotaIS.Text = string.Empty;
        TxtAliquotaIbsMunicipioDiferimento.Text = string.Empty;
        TxtAliquotaIbsMunicipioReducao.Text = string.Empty;
        TxtOrigemPadraoCategoria.Visibility = Visibility.Collapsed;
    }

    // --- Autocompletar de NCM (BrasilAPI — Tabela NCM oficial) ---

    /// <summary>Debounça 350ms após a última tecla antes de consultar — evita disparar uma
    /// chamada de rede a cada caractere digitado.</summary>
    private void TxtNcm_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_preenchendoNcmProgramaticamente || _suprimirEventosUi)
            return;

        _ncmDebounceTimer?.Stop();
        _ncmDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _ncmDebounceTimer.Tick -= NcmDebounceTimer_Tick;
        _ncmDebounceTimer.Tick += NcmDebounceTimer_Tick;
        _ncmDebounceTimer.Start();
    }

    private async void NcmDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _ncmDebounceTimer!.Stop();
        await BuscarSugestoesNcmAsync(TxtNcm.Text);
    }

    private async Task BuscarSugestoesNcmAsync(string termo)
    {
        // Cancela uma busca anterior ainda em andamento — só a última letra digitada importa.
        _ncmCts?.Cancel();
        _ncmCts?.Dispose();
        _ncmCts = new CancellationTokenSource();
        var token = _ncmCts.Token;

        if (termo.Trim().Length < 2)
        {
            PopupSugestoesNcm.IsOpen = false;
            return;
        }

        try
        {
            var sugestoes = await _ncmService.BuscarAsync(termo, token);
            if (token.IsCancellationRequested) return;

            LstSugestoesNcm.ItemsSource = sugestoes;
            PopupSugestoesNcm.IsOpen = sugestoes.Count > 0;
        }
        catch (OperationCanceledException)
        {
            // Busca superada por uma tecla mais recente — nada a fazer.
        }
        catch
        {
            // Consulta auxiliar (sem internet, BrasilAPI fora do ar) — o operador ainda
            // digita o NCM manualmente, não vale a pena interromper o cadastro por isso.
            PopupSugestoesNcm.IsOpen = false;
        }
    }

    private void LstSugestoesNcm_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstSugestoesNcm.SelectedItem is not NcmSugestaoDto selecionado)
            return;

        DefinirNcmSemAutocomplete(selecionado.Codigo);
        PopupSugestoesNcm.IsOpen = false;
        LstSugestoesNcm.SelectedItem = null;
    }

    /// <summary>Único ponto que atribui <c>TxtNcm.Text</c> fora da digitação do operador —
    /// suprime o <see cref="TxtNcm_TextChanged"/> pra um preenchimento programático (carregar
    /// produto existente, herdar padrão da categoria, limpar o formulário, ou aplicar a
    /// sugestão escolhida) não reabrir o popup de busca em cima do próprio resultado.</summary>
    private void DefinirNcmSemAutocomplete(string valor)
    {
        _preenchendoNcmProgramaticamente = true;
        try
        {
            TxtNcm.Text = valor;
            TxtNcm.CaretIndex = TxtNcm.Text.Length;
        }
        finally
        {
            _preenchendoNcmProgramaticamente = false;
        }
    }

    private void DefinirOrigemSelecionada(OrigemMercadoria? origem)
    {
        if (origem is null)
        {
            CmbOrigem.SelectedItem = null;
            return;
        }

        foreach (ComboBoxItem item in CmbOrigem.Items)
        {
            if (item.Tag is string tag && int.TryParse(tag, out var valor) && valor == (int)origem.Value)
            {
                CmbOrigem.SelectedItem = item;
                return;
            }
        }
    }

    private OrigemMercadoria? ObterOrigemSelecionada()
    {
        var tag = (CmbOrigem.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return int.TryParse(tag, out var valor) ? (OrigemMercadoria)valor : null;
    }

    private static string FormatarPercentual(decimal? valor)
        => valor.HasValue ? valor.Value.ToString("0.####", FormattingHelper.CulturaPtBr) : string.Empty;

    /// <summary>Monta o DTO de tributação a partir dos campos preenchidos na tela.
    /// Retorna null se nenhum campo fiscal foi preenchido (nada a salvar).
    /// Lança <see cref="DomainException"/> quando algum percentual digitado não é um número
    /// válido — antes o texto inválido virava null em silêncio e o produto era salvo "sem
    /// alíquota", travando a emissão da nota sem nenhum aviso no cadastro.</summary>
    private TributacaoProdutoDto? MontarDtoTributacaoOuNulo()
    {
        // As alíquotas contam como "campo preenchido": digitar só a alíquota de ICMS (o caso
        // clássico de quem está corrigindo a nota bloqueada por falta de pICMS) devolvia null
        // aqui, e o salvamento da tributação era pulado sem qualquer mensagem.
        var temAlgumCampo =
            !string.IsNullOrWhiteSpace(TxtAliquotaIcms.Text) ||
            !string.IsNullOrWhiteSpace(TxtAliquotaIcmsSt.Text) ||
            !string.IsNullOrWhiteSpace(TxtAliquotaPis.Text) ||
            !string.IsNullOrWhiteSpace(TxtAliquotaCofins.Text) ||
            !string.IsNullOrWhiteSpace(TxtAliquotaIpi.Text) ||
            !string.IsNullOrWhiteSpace(TxtMva.Text) ||
            !string.IsNullOrWhiteSpace(TxtReducaoBaseCalculo.Text) ||
            !string.IsNullOrWhiteSpace(TxtAliquotaIcmsStRetido.Text) ||
            !string.IsNullOrWhiteSpace(TxtAliquotaIS.Text) ||
            !string.IsNullOrWhiteSpace(TxtNcm.Text) ||
            !string.IsNullOrWhiteSpace(TxtCest.Text) ||
            CmbOrigem.SelectedItem is not null ||
            !string.IsNullOrWhiteSpace(TxtCstIcms.Text) ||
            !string.IsNullOrWhiteSpace(TxtCsosnIcms.Text) ||
            !string.IsNullOrWhiteSpace(TxtCstPis.Text) ||
            !string.IsNullOrWhiteSpace(TxtCstCofins.Text) ||
            !string.IsNullOrWhiteSpace(TxtCstIpi.Text) ||
            !string.IsNullOrWhiteSpace(TxtUnidadeTributavel.Text) ||
            !string.IsNullOrWhiteSpace(TxtGtinTributavel.Text) ||
            !string.IsNullOrWhiteSpace(TxtCfopDentroEstado.Text) ||
            !string.IsNullOrWhiteSpace(TxtCfopForaEstado.Text) ||
            !string.IsNullOrWhiteSpace(TxtCstIbsCbs.Text) ||
            !string.IsNullOrWhiteSpace(TxtCClassTrib.Text) ||
            !string.IsNullOrWhiteSpace(TxtCstIS.Text) ||
            !string.IsNullOrWhiteSpace(TxtCClassTribIS.Text) ||
            !string.IsNullOrWhiteSpace(TxtValorIpiFixo.Text) ||
            !string.IsNullOrWhiteSpace(TxtExTipi.Text);

        if (!temAlgumCampo)
            return null;

        var aliquotaIcms = LerNumeroOpcional(TxtAliquotaIcms, "Alíquota de ICMS");
        var aliquotaIcmsSt = LerNumeroOpcional(TxtAliquotaIcmsSt, "Alíquota de ICMS-ST");
        var mva = LerNumeroOpcional(TxtMva, "MVA/IVA-ST");
        var reducaoBase = LerNumeroOpcional(TxtReducaoBaseCalculo, "Redução de base de cálculo");
        var aliquotaIcmsStRetido = LerNumeroOpcional(TxtAliquotaIcmsStRetido, "Alíquota de ICMS-ST Retido (pST)");
        var aliquotaPis = LerNumeroOpcional(TxtAliquotaPis, "Alíquota de PIS");
        var aliquotaCofins = LerNumeroOpcional(TxtAliquotaCofins, "Alíquota de COFINS");
        var aliquotaIpi = LerNumeroOpcional(TxtAliquotaIpi, "Alíquota de IPI");
        var valorIpiFixo = LerNumeroOpcional(TxtValorIpiFixo, "Valor fixo do IPI");
        var fatorConversao = LerNumeroOpcional(TxtFatorConversao, "Fator de conversão");
        var aliquotaIS = LerNumeroOpcional(TxtAliquotaIS, "Alíquota do Imposto Seletivo");
        var aliquotaIbsMunicipioDiferimento = LerNumeroOpcional(TxtAliquotaIbsMunicipioDiferimento, "Alíquota de diferimento do IBS Municipal");
        var aliquotaIbsMunicipioReducao = LerNumeroOpcional(TxtAliquotaIbsMunicipioReducao, "Redução de alíquota do IBS Municipal");

        return new TributacaoProdutoDto
        {
            Ncm = TextoOuNulo(TxtNcm.Text),
            Cest = TextoOuNulo(TxtCest.Text),
            Origem = ObterOrigemSelecionada(),
            CstIcms = TextoOuNulo(TxtCstIcms.Text),
            CsosnIcms = TextoOuNulo(TxtCsosnIcms.Text),
            AliquotaIcms = aliquotaIcms,
            AliquotaIcmsSt = aliquotaIcmsSt,
            Mva = mva,
            ReducaoBaseCalculo = reducaoBase,
            AliquotaIcmsStRetido = aliquotaIcmsStRetido,
            CstPis = TextoOuNulo(TxtCstPis.Text),
            AliquotaPis = aliquotaPis,
            CstCofins = TextoOuNulo(TxtCstCofins.Text),
            AliquotaCofins = aliquotaCofins,
            CstIpi = TextoOuNulo(TxtCstIpi.Text),
            CodigoEnquadramentoIpi = TextoOuNulo(TxtCodigoEnquadramentoIpi.Text),
            AliquotaIpi = aliquotaIpi,
            ValorIpiFixo = valorIpiFixo,
            ExTipi = TextoOuNulo(TxtExTipi.Text),
            UnidadeTributavel = TextoOuNulo(TxtUnidadeTributavel.Text),
            FatorConversao = fatorConversao,
            GtinTributavel = TextoOuNulo(TxtGtinTributavel.Text),
            CfopDentroEstado = TextoOuNulo(TxtCfopDentroEstado.Text),
            CfopForaEstado = TextoOuNulo(TxtCfopForaEstado.Text),
            CstIbsCbs = TextoOuNulo(TxtCstIbsCbs.Text),
            CClassTrib = TextoOuNulo(TxtCClassTrib.Text),
            CstIS = TextoOuNulo(TxtCstIS.Text),
            CClassTribIS = TextoOuNulo(TxtCClassTribIS.Text),
            AliquotaIS = aliquotaIS,
            AliquotaIbsMunicipioDiferimento = aliquotaIbsMunicipioDiferimento,
            AliquotaIbsMunicipioReducao = aliquotaIbsMunicipioReducao
        };
    }

    private static string? TextoOuNulo(string texto)
        => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>Campo numérico opcional da aba Tributação: vazio vira null, texto inválido
    /// vira erro visível. Silenciar o erro aqui significa gravar "sem alíquota" num produto
    /// que o operador acabou de preencher.</summary>
    private static decimal? LerNumeroOpcional(TextBox campo, string rotulo)
    {
        if (FormattingHelper.TryParseMoedaOpcional(campo.Text, out var valor))
            return valor;

        throw new DomainException(
            $"{rotulo}: '{campo.Text}' não é um número válido — use apenas dígitos e vírgula (ex.: 18 ou 18,5), sem o símbolo '%'.");
    }

    private async void TxtNome_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_produtoId.HasValue || _codigoDefinidoManualmente || _codigoDesbloqueadoManualmente)
            return;

        var nome = TxtNome.Text.Trim();
        if (string.IsNullOrWhiteSpace(nome))
            return;

        await GerarCodigoPorNomeAsync(nome);
    }

    private async Task GerarCodigoPorNomeAsync(string nome)
    {
        var codigo = await _produtoService.GerarCodigoInternoPorNomeAsync(nome);
        _codigoDefinidoManualmente = false;
        DefinirCodigoInternoSemMarcarManual(codigo);
        _ultimoCodigoGeradoAutomaticamente = codigo;
    }

    private void BtnAlternarCodigoManual_Click(object sender, RoutedEventArgs e)
    {
        _codigoDesbloqueadoManualmente = !_codigoDesbloqueadoManualmente;
        if (!_codigoDesbloqueadoManualmente && !_produtoId.HasValue)
            _codigoDefinidoManualmente = false;

        AplicarEstadoCampoCodigo();
    }

    private void AplicarEstadoCampoCodigo()
    {
        var editavel = _codigoDesbloqueadoManualmente;
        TxtCodigoInterno.IsReadOnly = !editavel;
        BtnAlternarCodigoManual.Content = editavel ? "🔓" : "🔒";
        BtnAlternarCodigoManual.ToolTip = editavel
            ? "Bloquear e voltar à geração automática pelo nome"
            : "Desbloquear edição manual do código";
    }

    private void DefinirCodigoInternoSemMarcarManual(string codigo)
    {
        _ignorarAlteracaoCodigoInterno = true;
        TxtCodigoInterno.Text = codigo;
        _ignorarAlteracaoCodigoInterno = false;
    }

    private void TxtCodigoInterno_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ignorarAlteracaoCodigoInterno || _produtoId.HasValue || !_codigoDesbloqueadoManualmente)
            return;

        var textoAtual = TxtCodigoInterno.Text.Trim();
        _codigoDefinidoManualmente = !string.Equals(
            textoAtual,
            _ultimoCodigoGeradoAutomaticamente,
            StringComparison.OrdinalIgnoreCase);
    }

    private void ChkPromocaoAtiva_Changed(object sender, RoutedEventArgs e)
    {
        if (PainelPrecoPromocional is null || ChkPromocaoAtiva is null)
            return;

        PainelPrecoPromocional.Visibility = ChkPromocaoAtiva.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ChkCustoTotal_Changed(object sender, RoutedEventArgs e)
    {
        _modoCustoTotal = ChkCustoTotal.IsChecked == true;
        PainelCustoTotal.Visibility = _modoCustoTotal ? Visibility.Visible : Visibility.Collapsed;
        TxtCusto.IsReadOnly = _modoCustoTotal;

        if (_modoCustoTotal)
            CalcularCustoUnitarioPeloTotal();
        else
            TxtCustoTotal.Text = string.Empty;
    }

    private void TxtQuantidade_TextChanged(object sender, TextChangedEventArgs e)
        => CalcularCustoUnitarioPeloTotal();

    private void TxtCustoTotal_TextChanged(object sender, TextChangedEventArgs e)
        => CalcularCustoUnitarioPeloTotal();

    private void TxtCusto_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suprimirAtualizacaoCusto || _modoCustoTotal)
            return;
    }

    private void CalcularCustoUnitarioPeloTotal()
    {
        if (!_modoCustoTotal)
            return;

        if (!FormattingHelper.TryParseQuantidade(TxtQuantidade.Text, out var quantidade) || quantidade <= 0)
        {
            DefinirCustoCalculado(null);
            return;
        }

        if (!FormattingHelper.TryParseMoeda(TxtCustoTotal.Text, out var total) || total <= 0)
        {
            DefinirCustoCalculado(null);
            return;
        }

        DefinirCustoCalculado(total / quantidade);
    }

    private void DefinirCustoCalculado(decimal? custoUnitario)
    {
        _suprimirAtualizacaoCusto = true;
        TxtCusto.Text = custoUnitario.HasValue
            ? FormattingHelper.FormatarMoedaEntrada(custoUnitario.Value)
            : string.Empty;
        _suprimirAtualizacaoCusto = false;
    }

    private async void BtnNovaCategoria_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NomeRapidoDialogView("Nova Categoria", "Nome da Categoria *");
        if (ModalWindowHelper.ExibirDialogo(dialog, this) != true || string.IsNullOrWhiteSpace(dialog.NomeInformado))
            return;

        try
        {
            var criada = await _categoriaService.CriarAsync(dialog.NomeInformado);
            await CarregarComboBoxesAsync(criada.Id, ObterIdSelecionado(CmbMarca), ObterIdSelecionado(CmbFornecedor));
            LimparErroValidacao();
        }
        catch (Exception ex)
        {
            ExibirErroValidacao(ExceptionMessageHelper.ObterMensagemAmigavel(ex));
        }
    }

    private async void BtnNovaMarca_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NomeRapidoDialogView("Nova Marca", "Nome da Marca *");
        if (ModalWindowHelper.ExibirDialogo(dialog, this) != true || string.IsNullOrWhiteSpace(dialog.NomeInformado))
            return;

        try
        {
            var criada = await _marcaService.CriarAsync(dialog.NomeInformado);
            await CarregarComboBoxesAsync(ObterIdSelecionado(CmbCategoria), criada.Id, ObterIdSelecionado(CmbFornecedor));
            LimparErroValidacao();
        }
        catch (Exception ex)
        {
            ExibirErroValidacao(ExceptionMessageHelper.ObterMensagemAmigavel(ex));
        }
    }

    private async void BtnSalvar_Click(object sender, RoutedEventArgs e)
    {
        LimparErroValidacao();

        if (!ValidarFormulario())
            return;

        if (!await ValidarCodigoBarrasDuplicadoAsync())
            return;

        try
        {
            FormattingHelper.TryParseMoedaOpcional(TxtCusto.Text, out decimal? custo);
            FormattingHelper.TryParseMoeda(TxtPrecoVenda.Text, out decimal preco);
            FormattingHelper.TryParseQuantidade(TxtQuantidade.Text, out decimal quantidade);
            FormattingHelper.TryParseQuantidade(TxtEstoqueMinimo.Text, out decimal estoqueMin);

            var promocaoAtiva = ChkPromocaoAtiva.IsChecked == true;
            decimal? precoPromocional = null;
            if (promocaoAtiva)
            {
                if (!FormattingHelper.TryParseMoeda(TxtPrecoPromocional.Text, out var promo) || promo <= 0)
                {
                    ExibirErroValidacao("Informe o preço promocional.");
                    return;
                }

                precoPromocional = promo;
            }

            var categoriaId = ObterIdSelecionado(CmbCategoria);
            var marcaId = ObterIdSelecionado(CmbMarca);
            var fornecedorId = ObterIdSelecionado(CmbFornecedor);
            var unidade = (CmbUnidade.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "UN";
            var tamanhoEmbalagem = string.IsNullOrWhiteSpace(TxtTamanhoEmbalagem.Text) ? null : TxtTamanhoEmbalagem.Text.Trim();

            if (!TentarLerPesoGramas(out var pesoGramas))
            {
                ExibirErroValidacao("Peso inválido — informe kg com até 3 casas decimais (ex.: 5,500).");
                return;
            }

            if (!PesoProdutoHelper.TentarLerCentimetros(TxtAlturaCm.Text, out var altura)
                || !PesoProdutoHelper.TentarLerCentimetros(TxtLarguraCm.Text, out var largura)
                || !PesoProdutoHelper.TentarLerCentimetros(TxtComprimentoCm.Text, out var comprimento))
            {
                ExibirErroValidacao("Dimensões inválidas — informe valores maiores que zero em cm, com até 2 casas decimais.");
                return;
            }
            if (!_produtoId.HasValue && (!pesoGramas.HasValue || !altura.HasValue || !largura.HasValue || !comprimento.HasValue))
            {
                ExibirErroValidacao("Informe peso, altura, largura e comprimento para cadastrar um novo produto.");
                return;
            }

            var tributacaoDto = MontarDtoTributacaoOuNulo();
            if (tributacaoDto is not null)
                TributacaoProdutoValidator.Validar(tributacaoDto, _regimeAtual);

            BtnSalvar.IsEnabled = false;
            BtnSalvar.Content = "Salvando...";

            int produtoIdSalvo;

            if (_produtoId.HasValue)
            {
                var dto = new AtualizarProdutoDto
                {
                    Id = _produtoId.Value,
                    CodigoInterno = TxtCodigoInterno.Text.Trim(),
                    CodigoBarras = string.IsNullOrWhiteSpace(TxtCodigoBarras.Text) ? null : TxtCodigoBarras.Text.Trim(),
                    Nome = TxtNome.Text.Trim(),
                    CategoriaId = categoriaId,
                    MarcaId = marcaId,
                    FornecedorId = fornecedorId,
                    DataValidade = DpValidade.SelectedDate,
                    QuantidadeEstoque = quantidade,
                    EstoqueMinimo = estoqueMin,
                    Unidade = unidade,
                    TamanhoEmbalagem = tamanhoEmbalagem,
                    PesoGramas = pesoGramas,
                    AlturaCm = altura,
                    LarguraCm = largura,
                    ComprimentoCm = comprimento,
                    AlterarImagem = _imagemSelecionada is not null,
                    ImagemArquivoSelecionado = _imagemSelecionada,
                    RemoverImagem = _removerImagem,
                    Custo = custo,
                    PrecoVenda = preco,
                    PromocaoAtiva = promocaoAtiva,
                    PrecoPromocional = precoPromocional,
                    Observacoes = string.IsNullOrWhiteSpace(TxtObservacoes.Text) ? null : TxtObservacoes.Text.Trim(),
                    QuantidadeEstoqueOriginal = _quantidadeEstoqueCarregadaNaEdicao
                };
                await _produtoService.AtualizarAsync(_produtoId.Value, dto);
                produtoIdSalvo = _produtoId.Value;
            }
            else
            {
                var dto = new CriarProdutoDto
                {
                    CodigoInterno = TxtCodigoInterno.Text.Trim(),
                    CodigoInternoDefinidoManualmente = _codigoDefinidoManualmente,
                    CodigoBarras = string.IsNullOrWhiteSpace(TxtCodigoBarras.Text) ? null : TxtCodigoBarras.Text.Trim(),
                    Nome = TxtNome.Text.Trim(),
                    CategoriaId = categoriaId,
                    MarcaId = marcaId,
                    FornecedorId = fornecedorId,
                    DataValidade = DpValidade.SelectedDate,
                    QuantidadeEstoque = quantidade,
                    EstoqueMinimo = estoqueMin,
                    Unidade = unidade,
                    TamanhoEmbalagem = tamanhoEmbalagem,
                    PesoGramas = pesoGramas,
                    AlturaCm = altura,
                    LarguraCm = largura,
                    ComprimentoCm = comprimento,
                    AlterarImagem = _imagemSelecionada is not null,
                    ImagemArquivoSelecionado = _imagemSelecionada,
                    RemoverImagem = _removerImagem,
                    Custo = custo,
                    PrecoVenda = preco,
                    PromocaoAtiva = promocaoAtiva,
                    PrecoPromocional = precoPromocional,
                    Observacoes = string.IsNullOrWhiteSpace(TxtObservacoes.Text) ? null : TxtObservacoes.Text.Trim()
                };
                var criado = await _produtoService.CriarAsync(dto);
                produtoIdSalvo = criado.Id;
            }

            if (tributacaoDto is not null)
                await _produtoService.SalvarTributacaoAsync(produtoIdSalvo, tributacaoDto);

            DialogResult = true;
            Close();
        }
        catch (DomainException ex)
        {
            ExibirErroValidacao(ex.Message);
        }
        catch (Exception ex)
        {
            ExibirErroValidacao(ExceptionMessageHelper.ObterMensagemAmigavel(ex));
        }
        finally
        {
            BtnSalvar.IsEnabled = true;
            BtnSalvar.Content = "Salvar Produto";
        }
    }

    private bool ValidarFormulario()
    {
        if (string.IsNullOrWhiteSpace(TxtNome.Text))
        {
            ExibirErroValidacao("Nome do produto é obrigatório.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(TxtCodigoInterno.Text))
        {
            ExibirErroValidacao("Código do produto é obrigatório. Preencha o nome para gerar automaticamente ou desbloqueie o campo.");
            return false;
        }

        var categoriaId = ObterIdSelecionado(CmbCategoria);
        var marcaId = ObterIdSelecionado(CmbMarca);
        if (!categoriaId.HasValue || !marcaId.HasValue)
        {
            ExibirErroValidacao("Selecione uma Categoria e uma Marca válidas.");
            return false;
        }

        if (!FormattingHelper.TryParseMoedaOpcional(TxtCusto.Text, out decimal? custo))
        {
            ExibirErroValidacao("Preço de custo inválido.");
            return false;
        }

        if (_modoCustoTotal)
        {
            if (!FormattingHelper.TryParseMoeda(TxtCustoTotal.Text, out var totalCompra) || totalCompra <= 0)
            {
                ExibirErroValidacao("Informe o valor total da compra.");
                return false;
            }

            if (!FormattingHelper.TryParseQuantidade(TxtQuantidade.Text, out var quantidadeCompra) || quantidadeCompra <= 0)
            {
                ExibirErroValidacao("Informe a quantidade em estoque para calcular o custo unitário.");
                return false;
            }

            CalcularCustoUnitarioPeloTotal();
            FormattingHelper.TryParseMoedaOpcional(TxtCusto.Text, out custo);
        }

        if (custo is <= 0 && (_modoCustoTotal || !string.IsNullOrWhiteSpace(TxtCusto.Text)))
        {
            ExibirErroValidacao("Preço de custo, quando informado, deve ser maior que zero.");
            return false;
        }

        if (!FormattingHelper.TryParseMoeda(TxtPrecoVenda.Text, out decimal preco) || preco <= 0)
        {
            ExibirErroValidacao("Preço de venda deve ser maior que zero.");
            return false;
        }

        if (ChkPromocaoAtiva.IsChecked == true)
        {
            if (!FormattingHelper.TryParseMoeda(TxtPrecoPromocional.Text, out var promo) || promo <= 0)
            {
                ExibirErroValidacao("Informe o preço promocional.");
                return false;
            }

            if (promo > preco)
            {
                ExibirErroValidacao("O preço promocional não pode ser maior que o preço de venda padrão.");
                return false;
            }
        }

        if (!FormattingHelper.TryParseQuantidade(TxtQuantidade.Text, out decimal quantidade) || quantidade < 0)
        {
            ExibirErroValidacao("Quantidade em estoque não pode ser negativa.");
            return false;
        }

        if (!FormattingHelper.TryParseQuantidade(TxtEstoqueMinimo.Text, out decimal estoqueMin) || estoqueMin < 0)
        {
            ExibirErroValidacao("Estoque mínimo não pode ser negativo.");
            return false;
        }

        return true;
    }

    private async void TxtCodigoBarras_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtCodigoBarras.Text))
            return;

        await ValidarCodigoBarrasDuplicadoAsync();
    }

    private async Task<bool> ValidarCodigoBarrasDuplicadoAsync()
    {
        var codigo = TxtCodigoBarras.Text.Trim();
        if (string.IsNullOrWhiteSpace(codigo))
            return true;

        var existe = await _produtoService.CodigoBarrasExisteAsync(codigo, _produtoId);
        if (!existe)
            return true;

        ExibirErroValidacao(ProdutoService.MensagemCodigoBarrasDuplicado);
        TxtCodigoBarras.Focus();
        return false;
    }

    private void TxtFrete_TextChanged(object sender, TextChangedEventArgs e)
        => AtualizarPesoEquivalente();

    private void AtualizarPesoEquivalente()
    {
        if (TxtPesoEquivalente is null || TxtFretePendente is null || TxtComprimentoCm is null)
            return;
        var valido = TentarLerPesoGramas(out var gramas);
        TxtPesoEquivalente.Text = valido && gramas.HasValue ? $"= {gramas.Value} g" : string.Empty;
        TxtPesoEquivalente.Visibility = TxtPesoEquivalente.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var pronto = valido && gramas is > 0
            && PesoProdutoHelper.TentarLerCentimetros(TxtAlturaCm.Text, out var a) && a.HasValue
            && PesoProdutoHelper.TentarLerCentimetros(TxtLarguraCm.Text, out var l) && l.HasValue
            && PesoProdutoHelper.TentarLerCentimetros(TxtComprimentoCm.Text, out var c) && c.HasValue;
        TxtFretePendente.Text = pronto ? "Pronto para frete" : "Dados de frete pendentes";
        TxtFretePendente.SetResourceReference(TextBlock.ForegroundProperty, pronto ? "VerdeSucessoBrush" : "VermelhoErroBrush");
    }

    private bool TentarLerPesoGramas(out int? pesoGramas)
        => PesoProdutoHelper.TentarLerQuilos(TxtPesoKg.Text, out pesoGramas);

    private void BtnSelecionarImagem_Click(object sender, RoutedEventArgs e)
    {
        var arquivo = _dialogs.SelecionarImagem(this);
        if (arquivo is null) return;
        try
        {
            _imagens.ValidarArquivo(arquivo);
            _imagemSelecionada = arquivo;
            _removerImagem = false;
            AtualizarImagem();
        }
        catch (DomainException ex) { ExibirErroValidacao(ex.Message); }
        catch { ExibirErroValidacao("Não foi possível carregar a imagem selecionada."); }
    }

    private void BtnRemoverImagem_Click(object sender, RoutedEventArgs e)
    {
        _imagemSelecionada = null;
        _removerImagem = true;
        AtualizarImagem();
    }

    private void AtualizarImagem()
    {
        if (ImgProduto is null || TxtImagemEstado is null) return;
        ImgProduto.Source = null;
        var possui = !_removerImagem && (_imagemSelecionada is not null || _imagemReferencia is not null);
        BtnSelecionarImagem.Content = possui ? "Alterar imagem" : "Selecionar imagem";
        BtnRemoverImagem.IsEnabled = possui;
        TxtImagemEstado.Text = _removerImagem ? "Remoção pendente — será confirmada ao salvar."
            : possui ? "Imagem principal do produto" : "Produto sem imagem";
        if (!possui) return;
        try
        {
            var arquivo = _imagemSelecionada ?? _imagens.ObterCaminhoSeguro(_imagemReferencia);
            if (arquivo is null || !File.Exists(arquivo))
            {
                TxtImagemEstado.Text = "Imagem pendente — arquivo não encontrado. Selecione novamente.";
                return;
            }
            using var stream = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.Read);
            var preview = new BitmapImage();
            preview.BeginInit();
            preview.CacheOption = BitmapCacheOption.OnLoad;
            preview.DecodePixelWidth = 240;
            preview.StreamSource = stream;
            preview.EndInit();
            preview.Freeze();
            ImgProduto.Source = preview;
        }
        catch { TxtImagemEstado.Text = "Imagem pendente — arquivo inválido ou indisponível."; }
    }
    private static int? ObterIdSelecionado(ComboBox combo)
    {
        return combo.SelectedValue switch
        {
            int id when id > 0 => id,
            long id when id > 0 => (int)id,
            _ when combo.SelectedItem is CategoriaDto c && c.Id > 0 => c.Id,
            _ when combo.SelectedItem is MarcaDto m && m.Id > 0 => m.Id,
            _ when combo.SelectedItem is FornecedorDto f && f.Id > 0 => f.Id,
            _ => null
        };
    }

    private void ExibirErroValidacao(string mensagem)
    {
        TxtErroValidacao.Text = mensagem;
        TxtErroValidacao.Visibility = Visibility.Visible;
        _dialogs.MostrarValidacao(this, mensagem);
    }

    private void LimparErroValidacao()
    {
        TxtErroValidacao.Text = string.Empty;
        TxtErroValidacao.Visibility = Visibility.Collapsed;
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
