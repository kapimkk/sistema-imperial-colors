using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Exceptions;
using ImperialColors.UI.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ImperialColors.UI.Views;

public partial class NotaFiscalFormView : Window
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INotaFiscalService _notaFiscalService;
    private readonly IClienteService _clienteService;
    private readonly IProdutoService _produtoService;
    private readonly INaturezaOperacaoService _naturezaOperacaoService;
    private readonly IConfiguracaoFiscalService _configuracaoFiscal;
    private readonly IViaCepService _viaCepService;
    private readonly TipoNotaFiscal _tipo;
    private readonly int? _notaFiscalId;

    /// <summary>Rascunho montado fora desta tela (hoje, a partir de uma venda — ver
    /// <see cref="INotaFiscalService.MontarRascunhoAPartirDeVendaAsync"/>) e ainda não
    /// persistido. Quando presente, substitui a nota em branco que a tela criaria.</summary>
    private readonly NotaFiscalDto? _rascunhoInicial;

    /// <summary>Id da nota depois de salva — o chamador usa para abrir "Ações da Nota" e
    /// emitir na sequência, sem obrigar o operador a procurar a nota na lista.</summary>
    public int? NotaFiscalSalvaId { get; private set; }

    /// <summary>Marcado pelo chamador que já encadeia "Ações da Nota" depois de salvar
    /// (faturamento de venda). Cala o aviso de "abra Ações da Nota na lista", que só faz
    /// sentido para quem salvou o rascunho e ficou onde estava.</summary>
    public bool ChamadorAbreAcoesAposSalvar { get; set; }

    private NotaFiscalDto _nota = new();
    private readonly ObservableCollection<ItemNotaFiscalDto> _itens = new();
    private readonly ObservableCollection<PagamentoUi> _pagamentos = new();
    private ConfiguracaoFiscalEmpresaDto _empresaFiscal = new();
    private bool _carregando = true;
    private bool _atualizandoCalculo;

    public NotaFiscalFormView(IServiceProvider serviceProvider, TipoNotaFiscal tipo, int? notaFiscalId)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        _notaFiscalService = serviceProvider.GetRequiredService<INotaFiscalService>();
        _clienteService = serviceProvider.GetRequiredService<IClienteService>();
        _produtoService = serviceProvider.GetRequiredService<IProdutoService>();
        _naturezaOperacaoService = serviceProvider.GetRequiredService<INaturezaOperacaoService>();
        _configuracaoFiscal = serviceProvider.GetRequiredService<IConfiguracaoFiscalService>();
        _viaCepService = serviceProvider.GetRequiredService<IViaCepService>();
        _tipo = tipo;
        _notaFiscalId = notaFiscalId;

        TxtTitulo.Text = tipo == TipoNotaFiscal.NFCe
            ? (notaFiscalId.HasValue ? "Editar NFC-e" : "Cadastrar NFC-e")
            : (notaFiscalId.HasValue ? "Editar NF-e" : "Cadastrar NF-e");

        if (tipo == TipoNotaFiscal.NFCe)
            TxtAvisoDestinatario.Text = "Na NFC-e o destinatário é opcional — venda de balcão sem CPF/CNPJ do comprador pode ficar em branco.";

        GridItens.ItemsSource = _itens;
        GridPagamentos.ItemsSource = _pagamentos;

        PopularCombos();
        Loaded += async (_, _) => await CarregarAsync();
    }

    /// <summary>
    /// Abre a tela já preenchida com um rascunho montado a partir de uma venda. A nota ainda
    /// não existe no banco (Id 0): o operador revisa destinatário/itens/impostos e só então
    /// "Salvar Rascunho" a persiste — mesmo caminho de uma nota digitada à mão daqui para
    /// frente.
    /// </summary>
    public NotaFiscalFormView(IServiceProvider serviceProvider, NotaFiscalDto rascunhoDeVenda, string numeroVenda)
        : this(serviceProvider, rascunhoDeVenda.Tipo, notaFiscalId: null)
    {
        _rascunhoInicial = rascunhoDeVenda;
        ChamadorAbreAcoesAposSalvar = true;
        TxtTitulo.Text = $"{(rascunhoDeVenda.Tipo == TipoNotaFiscal.NFCe ? "NFC-e" : "NF-e")} da venda #{numeroVenda}";
    }

    private void PopularCombos()
    {
        // NFC-e é venda de balcão/PDV — na prática só existe "NF-e normal"; complementar,
        // ajuste e devolução são fluxos de correção B2B que não se aplicam a cupom fiscal.
        var finalidades = _tipo == TipoNotaFiscal.NFCe
            ? new[] { FinalidadeNfe.Normal }
            : Enum.GetValues<FinalidadeNfe>();
        var rotuloModelo = _tipo == TipoNotaFiscal.NFCe ? "NFC-e" : "NF-e";
        CmbFinalidade.ItemsSource = finalidades.Select(v => new EnumItem<FinalidadeNfe>(v, DescricaoFinalidade(v, rotuloModelo))).ToList();
        CmbFinalidade.SelectedIndex = 0;
        CmbFinalidade.IsEnabled = _tipo != TipoNotaFiscal.NFCe;

        CmbIndicadorPresenca.ItemsSource = Enum.GetValues<IndicadorPresencaComprador>().Select(v => new EnumItem<IndicadorPresencaComprador>(v, DescricaoIndicadorPresenca(v))).ToList();
        CmbIndicadorPresenca.SelectedIndex = 0;

        // Rótulos deixam explícito o que cada opção representa (e não só o nome técnico da
        // SEFAZ) — "Não informado"/"Isento"/"Não contribuinte" pareciam dizer "cliente sem
        // IE", quando na prática só controlam se o campo IE ao lado é enviado ou descartado
        // (ver NotaFiscalPayloadBuilder.ConstruirDest e o aviso em ValidarParaEmissao).
        CmbIndicadorIe.ItemsSource = new[]
        {
            new EnumItem<IndicadorIeDestinatario?>(null, "Não informado (não enviar IE)"),
            new EnumItem<IndicadorIeDestinatario?>(IndicadorIeDestinatario.ContribuinteIcms, "Contribuinte de ICMS (empresa com IE ativa)"),
            new EnumItem<IndicadorIeDestinatario?>(IndicadorIeDestinatario.Isento, "Isento de Inscrição Estadual"),
            new EnumItem<IndicadorIeDestinatario?>(IndicadorIeDestinatario.NaoContribuinte, "Não contribuinte (pessoa física / sem cadastro de ICMS)")
        };
        CmbIndicadorIe.SelectedIndex = _tipo == TipoNotaFiscal.NFCe ? 3 : 0;
        AtualizarCampoIePorIndicador();

        CmbFormaEnvio.ItemsSource = Enum.GetValues<ModalidadeFrete>().Select(v => new EnumItem<ModalidadeFrete>(v, DescricaoFrete(v))).ToList();
        CmbFormaEnvio.SelectedIndex = Array.IndexOf(Enum.GetValues<ModalidadeFrete>(), ModalidadeFrete.SemOcorrenciaTransporte);

        CmbNovaFormaPagamento.ItemsSource = Enum.GetValues<FormaPagamento>().Select(v => new EnumItem<FormaPagamento>(v, v.ToString())).ToList();
        CmbNovaFormaPagamento.SelectedIndex = 0;
    }

    /// <summary>
    /// A IE só é enviada à SEFAZ quando o indicador é "Contribuinte de ICMS" — ver
    /// NotaFiscalPayloadBuilder.ConstruirDest e o bloqueio equivalente em
    /// NotaFiscalValidator.ValidarParaEmissao. Desabilitar (sem apagar) o campo nos outros
    /// casos deixa visualmente óbvio que o valor ali não será usado, em vez do operador
    /// digitar uma IE, tudo parecer preenchido, e só descobrir que foi ignorada quando a
    /// SEFAZ rejeitar a nota com "IE do destinatário não informada".
    /// </summary>
    private void AtualizarCampoIePorIndicador()
    {
        var indicador = (CmbIndicadorIe.SelectedItem as EnumItem<IndicadorIeDestinatario?>)?.Valor;
        TxtDestinatarioIe.IsEnabled = indicador == IndicadorIeDestinatario.ContribuinteIcms;
    }

    private void CmbIndicadorIe_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => AtualizarCampoIePorIndicador();

    /// <summary>
    /// Contrapartida do método acima: se o operador digita uma IE de verdade, o indicador
    /// quase sempre deveria ser "Contribuinte de ICMS" — sem isso, alguém que preenche a IE
    /// mas esquece de trocar o indicador (ex.: cliente puxado do cadastro com "Não
    /// contribuinte" selecionado por padrão) só descobre o problema numa rejeição da SEFAZ.
    /// Só age com o operador digitando (guardado por <see cref="_carregando"/>) — nunca
    /// sobrescreve o indicador ao carregar uma nota/cliente já salvo.
    /// </summary>
    private void TxtDestinatarioIe_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_carregando) return;
        if (string.IsNullOrWhiteSpace(TxtDestinatarioIe.Text)) return;

        var indicador = (CmbIndicadorIe.SelectedItem as EnumItem<IndicadorIeDestinatario?>)?.Valor;
        if (indicador != IndicadorIeDestinatario.ContribuinteIcms)
            SelecionarEnum<IndicadorIeDestinatario>(CmbIndicadorIe, IndicadorIeDestinatario.ContribuinteIcms);
    }

    private async Task CarregarAsync()
    {
        try
        {
            var naturezas = (await _naturezaOperacaoService.ObterTodosAsync()).ToList();
            var itensCombo = new List<NaturezaItem> { new(null, "(nenhuma selecionada)") };
            itensCombo.AddRange(naturezas.Select(n => new NaturezaItem(n, n.Descricao)));
            CmbNatureza.ItemsSource = itensCombo;
            CmbNatureza.SelectedIndex = 0;

            _empresaFiscal = await _configuracaoFiscal.ObterConfiguracaoEmpresaAsync();

            if (_notaFiscalId.HasValue)
            {
                _nota = await _notaFiscalService.ObterPorIdAsync(_notaFiscalId.Value)
                    ?? throw new DomainException("Nota fiscal não encontrada.");

                // O rascunho pode ter sido criado antes de uma correção na tributação do
                // produto, na Regra Geral fiscal da empresa ou no regime tributário — sincroniza
                // itens e CRT com o cadastro ATUAL antes de exibir, para o operador não precisar
                // clicar "Atualizar" item por item só porque corrigiu algo em Estoque ou
                // Configurações → Fiscal. Só se aplica a notas ainda editáveis (Autorizada/
                // Cancelada/Indeterminada são documento fiscal ou já em trânsito com a SEFAZ).
                if (_nota.Status is StatusNotaFiscal.Rascunho or StatusNotaFiscal.Rejeitada)
                    _nota = await _notaFiscalService.SincronizarTributacaoComCadastroAtualAsync(_nota);
            }
            else if (_rascunhoInicial is not null)
            {
                // Já vem completo do Service (destinatário, itens com tributação atual,
                // pagamentos, numeração e totais) — só falta a revisão do operador. Repetir
                // aqui a montagem da nota em branco sobrescreveria tudo isso.
                _nota = _rascunhoInicial;
            }
            else
            {
                var crt = await _configuracaoFiscal.ObterCodigoCrtAsync();
                _nota = new NotaFiscalDto
                {
                    Tipo = _tipo,
                    Serie = string.IsNullOrWhiteSpace(_empresaFiscal.Serie) ? "1" : _empresaFiscal.Serie,
                    Crt = crt,
                    Ambiente = _empresaFiscal.Ambiente,
                    ConsumidorFinal = _tipo == TipoNotaFiscal.NFCe,
                    IndicadorPresenca = _empresaFiscal.IndicadorPresencaPadrao,
                    FormaEnvio = _empresaFiscal.FretePorContaPadrao,
                    DataEmissao = DateTime.Now
                };
                _nota.Numero = await _notaFiscalService.ObterProximoNumeroAsync(_tipo, _nota.Serie, _nota.Ambiente);
            }

            PreencherFormulario(_nota);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ExceptionMessageHelper.ObterMensagemAmigavel(ex), "Erro ao carregar nota",
                MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
            Close();
            return;
        }
        finally
        {
            _carregando = false;
        }
    }

    private void PreencherFormulario(NotaFiscalDto n)
    {
        TxtStatus.Text = DescricaoStatus(n.Status);
        TxtTipoSaida.Text = n.TipoSaida ?? "Emissão própria";
        TxtSerie.Text = n.Serie;
        TxtNumero.Text = n.Numero;
        TxtDataEmissao.Text = FormattingHelper.FormatarDataHora(n.DataEmissao);
        TxtDataSaida.Text = n.DataSaida.HasValue ? FormattingHelper.FormatarData(n.DataSaida.Value) : string.Empty;
        SelecionarEnum<FinalidadeNfe>(CmbFinalidade, n.Finalidade);
        TxtCrt.Text = n.Crt;
        ChkConsumidorFinal.IsChecked = n.ConsumidorFinal;
        SelecionarEnum<IndicadorPresencaComprador>(CmbIndicadorPresenca, n.IndicadorPresenca);
        TxtIntermediadorCnpj.Text = n.IntermediadorCnpj ?? string.Empty;
        TxtIntermediadorIdentificador.Text = n.IntermediadorIdentificador ?? string.Empty;

        if (n.NaturezaOperacaoId.HasValue && CmbNatureza.ItemsSource is IEnumerable<NaturezaItem> naturezas)
        {
            var item = naturezas.FirstOrDefault(x => x.Origem?.Id == n.NaturezaOperacaoId);
            if (item is not null) CmbNatureza.SelectedItem = item;
        }

        RbPessoaFisica.IsChecked = n.DestinatarioTipoPessoa != TipoPessoa.Juridica;
        RbPessoaJuridica.IsChecked = n.DestinatarioTipoPessoa == TipoPessoa.Juridica;
        TxtDestinatarioNome.Text = n.DestinatarioNome ?? string.Empty;
        TxtDestinatarioDocumento.Text = n.DestinatarioDocumento ?? string.Empty;
        SelecionarEnum(CmbIndicadorIe, n.DestinatarioIndicadorIe);
        TxtDestinatarioIe.Text = n.DestinatarioInscricaoEstadual ?? string.Empty;
        TxtSuframa.Text = n.Suframa ?? string.Empty;
        TxtDestinatarioCep.Text = n.DestinatarioCep ?? string.Empty;
        TxtDestinatarioLogradouro.Text = n.DestinatarioLogradouro ?? string.Empty;
        TxtDestinatarioNumero.Text = n.DestinatarioNumero ?? string.Empty;
        TxtDestinatarioComplemento.Text = n.DestinatarioComplemento ?? string.Empty;
        TxtDestinatarioBairro.Text = n.DestinatarioBairro ?? string.Empty;
        TxtDestinatarioCidade.Text = n.DestinatarioCidade ?? string.Empty;
        TxtDestinatarioUf.Text = n.DestinatarioUf ?? string.Empty;
        TxtDestinatarioCodigoIbge.Text = n.DestinatarioCodigoMunicipioIbge ?? string.Empty;
        TxtDestinatarioTelefone.Text = n.DestinatarioTelefone ?? string.Empty;
        TxtDestinatarioEmail.Text = n.DestinatarioEmail ?? string.Empty;
        TxtVendedor.Text = n.Vendedor ?? string.Empty;
        TxtListaPreco.Text = n.ListaPrecoNome ?? string.Empty;

        ChkEntregaDiferente.IsChecked = n.EntregaDiferenteCobranca;
        TxtEntregaCep.Text = n.EntregaCep ?? string.Empty;
        TxtEntregaLogradouro.Text = n.EntregaLogradouro ?? string.Empty;
        TxtEntregaNumero.Text = n.EntregaNumero ?? string.Empty;
        TxtEntregaComplemento.Text = n.EntregaComplemento ?? string.Empty;
        TxtEntregaBairro.Text = n.EntregaBairro ?? string.Empty;
        TxtEntregaCidade.Text = n.EntregaCidade ?? string.Empty;
        TxtEntregaUf.Text = n.EntregaUf ?? string.Empty;

        _itens.Clear();
        foreach (var item in n.Itens) _itens.Add(item);
        TxtAvisosItens.Text = string.Join(" | ", _itens.SelectMany(i => i.Avisos).Distinct());

        _pagamentos.Clear();
        foreach (var pagamento in n.Pagamentos) _pagamentos.Add(new PagamentoUi(pagamento));

        ChkCalculoAutomatico.IsChecked = n.CalculoAutomatico;
        AplicarPermissaoCalculo();
        AtualizarCamposCalculo(n);

        SelecionarEnum<ModalidadeFrete>(CmbFormaEnvio, n.FormaEnvio);
        TxtPesoBruto.Text = n.PesoBruto?.ToString("0.000") ?? string.Empty;
        TxtPesoLiquido.Text = n.PesoLiquido?.ToString("0.000") ?? string.Empty;
        ChkEnviarExpedicao.IsChecked = n.EnviarParaExpedicao;

        TxtFormaRecebimento.Text = n.FormaRecebimento ?? "Múltiplas";
        TxtCategoriaFinanceira.Text = n.CategoriaFinanceira ?? string.Empty;
        TxtCondicaoPagamento.Text = n.CondicaoPagamento ?? string.Empty;

        TxtDeposito.Text = n.Deposito ?? "Padrão";
        TxtObservacoes.Text = n.Observacoes ?? string.Empty;
        TxtObservacoesSistema.Text = n.ObservacoesSistema ?? string.Empty;
        TxtInformacoesFisco.Text = n.InformacoesFisco ?? string.Empty;
        TxtMarcadores.Text = n.Marcadores ?? string.Empty;

        AplicarPermissaoEdicao(n.PodeEditar);
    }

    private void AplicarPermissaoEdicao(bool podeEditar)
    {
        BtnSalvarRascunho.IsEnabled = podeEditar;
        BadgeStatus.Background = podeEditar
            ? (Brush)FindResource("AmareloPrimarioBrush")
            : (Brush)FindResource("VermelhoErroBrush");
    }

    private void AtualizarCamposCalculo(NotaFiscalDto n)
    {
        _atualizandoCalculo = true;
        try
        {
            TxtVProd.Text = n.VProd.ToString("0.00");
            TxtVFrete.Text = n.VFrete.ToString("0.00");
            TxtVSeg.Text = n.VSeg.ToString("0.00");
            TxtVBcIcms.Text = n.VBcIcms.ToString("0.00");
            TxtVIcms.Text = n.VIcms.ToString("0.00");
            TxtVBcIcmsSt.Text = n.VBcIcmsSt.ToString("0.00");
            TxtVIcmsSt.Text = n.VIcmsSt.ToString("0.00");
            TxtVIpi.Text = n.VIpi.ToString("0.00");
            TxtVIpiDevolvido.Text = n.VIpiDevolvido.ToString("0.00");
            TxtVOutro.Text = n.VOutro.ToString("0.00");
            TxtVDesc.Text = n.VDesc.ToString("0.00");
            TxtVFunrural.Text = n.VFunrural.ToString("0.00");
            TxtNumeroItens.Text = n.NumeroItens.ToString();
            TxtVAproxImp.Text = n.VAproxImp.ToString("0.00");
            TxtVFcp.Text = n.VFcp.ToString("0.00");
            TxtVFcpSt.Text = n.VFcpSt.ToString("0.00");
            TxtVFcpStRet.Text = n.VFcpStRet.ToString("0.00");
            TxtVNf.Text = n.VNf.ToString("0.00");
        }
        finally
        {
            _atualizandoCalculo = false;
        }
    }

    // --- Bloco Nota fiscal ---

    private void CmbNatureza_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_carregando || CmbNatureza.SelectedItem is not NaturezaItem { Origem: not null } item)
            return;

        var natureza = item.Origem;

        SelecionarEnum<FinalidadeNfe>(CmbFinalidade, natureza.Finalidade);
        ChkConsumidorFinal.IsChecked = natureza.ConsumidorFinal;
        if (!string.IsNullOrWhiteSpace(natureza.Serie))
            TxtSerie.Text = natureza.Serie;
        TxtObservacoesSistema.Text = natureza.ObservacoesPadrao ?? string.Empty;
    }

    // --- Bloco Destinatário ---

    private void TxtBuscaCliente_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BtnBuscarCliente_Click(sender, e);
    }

    private async void BtnBuscarCliente_Click(object sender, RoutedEventArgs e)
    {
        var termo = TxtBuscaCliente.Text?.Trim();
        if (string.IsNullOrWhiteSpace(termo)) return;

        try
        {
            var clientes = await _clienteService.BuscarAsync(termo);
            LstResultadosCliente.ItemsSource = clientes.Select(c => new ClienteResultadoUi(c)).ToList();
            LstResultadosCliente.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ExceptionMessageHelper.ObterMensagemAmigavel(ex), "Busca de cliente",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LstResultadosCliente_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstResultadosCliente.SelectedItem is not ClienteResultadoUi selecionado) return;
        var cliente = selecionado.Origem;

        RbPessoaFisica.IsChecked = cliente.TipoPessoa != TipoPessoa.Juridica;
        RbPessoaJuridica.IsChecked = cliente.TipoPessoa == TipoPessoa.Juridica;
        TxtDestinatarioNome.Text = cliente.Nome;
        TxtDestinatarioDocumento.Text = cliente.TipoPessoa == TipoPessoa.Juridica ? cliente.Cnpj : cliente.Cpf;
        SelecionarEnum(CmbIndicadorIe, cliente.IndicadorIe);
        TxtDestinatarioIe.Text = cliente.InscricaoEstadual ?? string.Empty;
        TxtDestinatarioCep.Text = cliente.Cep ?? string.Empty;
        TxtDestinatarioLogradouro.Text = cliente.Logradouro ?? string.Empty;
        TxtDestinatarioNumero.Text = cliente.Numero ?? string.Empty;
        TxtDestinatarioComplemento.Text = cliente.Complemento ?? string.Empty;
        TxtDestinatarioBairro.Text = cliente.Bairro ?? string.Empty;
        TxtDestinatarioCidade.Text = cliente.Cidade ?? string.Empty;
        TxtDestinatarioUf.Text = cliente.Estado ?? string.Empty;
        TxtDestinatarioCodigoIbge.Text = cliente.CodigoMunicipioIbge ?? string.Empty;
        TxtDestinatarioTelefone.Text = cliente.Telefone ?? string.Empty;
        TxtDestinatarioEmail.Text = cliente.Email ?? string.Empty;
        _nota.ClienteId = cliente.Id;

        LstResultadosCliente.Visibility = Visibility.Collapsed;
        TxtBuscaCliente.Text = string.Empty;
    }

    private async void BtnBuscarCepDestinatario_Click(object sender, RoutedEventArgs e)
    {
        var cep = TxtDestinatarioCep.Text?.Trim();
        if (string.IsNullOrWhiteSpace(cep)) return;

        try
        {
            var endereco = await _viaCepService.ConsultarAsync(cep);
            if (endereco is null)
            {
                MessageBox.Show("CEP não encontrado.", "Busca de CEP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtDestinatarioLogradouro.Text = endereco.Logradouro;
            TxtDestinatarioBairro.Text = endereco.Bairro;
            TxtDestinatarioCidade.Text = endereco.Cidade;
            TxtDestinatarioUf.Text = endereco.Uf;
            TxtDestinatarioCodigoIbge.Text = endereco.CodigoIbge ?? string.Empty;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ExceptionMessageHelper.ObterMensagemAmigavel(ex), "Busca de CEP",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ChkEntregaDiferente_Changed(object sender, RoutedEventArgs e)
        => PainelEntrega.Visibility = ChkEntregaDiferente.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    // --- Aba Produtos ---

    private void TxtBuscaProduto_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) BtnBuscarProduto_Click(sender, e);
    }

    private async void BtnBuscarProduto_Click(object sender, RoutedEventArgs e)
    {
        var termo = TxtBuscaProduto.Text?.Trim();
        if (string.IsNullOrWhiteSpace(termo)) return;

        try
        {
            var produtos = await _produtoService.BuscarAsync(termo);
            LstResultadosProduto.ItemsSource = produtos.Select(p => new ProdutoResultadoUi(p)).ToList();
            LstResultadosProduto.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ExceptionMessageHelper.ObterMensagemAmigavel(ex), "Busca de produto",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void LstResultadosProduto_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LstResultadosProduto.SelectedItem is not ProdutoResultadoUi selecionado) return;

        try
        {
            var ufEmitente = _empresaFiscal.Uf ?? string.Empty;
            var interestadual = !string.IsNullOrWhiteSpace(TxtDestinatarioUf.Text) &&
                !string.Equals(TxtDestinatarioUf.Text.Trim(), ufEmitente, StringComparison.OrdinalIgnoreCase);

            var item = await _notaFiscalService.MontarItemAPartirDeProdutoAsync(selecionado.Origem.Id, 1, interestadual);
            item.NItem = _itens.Count + 1;
            _itens.Add(item);

            if (item.Avisos.Count > 0)
                TxtAvisosItens.Text = string.Join(" | ", _itens.SelectMany(i => i.Avisos).Distinct());

            LstResultadosProduto.Visibility = Visibility.Collapsed;
            TxtBuscaProduto.Text = string.Empty;
            RecalcularTotaisUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ExceptionMessageHelper.ObterMensagemAmigavel(ex), "Adicionar item",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GridItens_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (e.Row.Item is not ItemNotaFiscalDto item) return;
            item.ValorTotal = Math.Round(item.Quantidade * item.ValorUnitario, 2, MidpointRounding.AwayFromZero);
            GridItens.Items.Refresh();
            RecalcularTotaisUi();
        }));
    }

    /// <summary>
    /// Traz de novo os dados fiscais do item (NCM, CFOP, CST/CSOSN, GTIN, preço etc.) a
    /// partir do cadastro atual do produto no estoque — sem precisar remover o item da nota,
    /// corrigir o produto em Estoque e readicioná-lo (fluxo antigo, que perdia a posição do
    /// item e obrigava a repetir a busca). Preserva a quantidade já lançada e a posição
    /// (NItem) do item na nota.
    /// </summary>
    private async void BtnAtualizarItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ItemNotaFiscalDto item }) return;

        if (item.ProdutoId is not int produtoId)
        {
            MessageBox.Show(
                "Este item não está vinculado a um produto do estoque — não há cadastro para atualizar.",
                "Atualizar item", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var ufEmitente = _empresaFiscal.Uf ?? string.Empty;
            var interestadual = !string.IsNullOrWhiteSpace(TxtDestinatarioUf.Text) &&
                !string.Equals(TxtDestinatarioUf.Text.Trim(), ufEmitente, StringComparison.OrdinalIgnoreCase);

            var atualizado = await _notaFiscalService.MontarItemAPartirDeProdutoAsync(produtoId, item.Quantidade, interestadual);
            atualizado.Id = item.Id;
            atualizado.NItem = item.NItem;

            var indice = _itens.IndexOf(item);
            if (indice >= 0) _itens[indice] = atualizado;

            TxtAvisosItens.Text = string.Join(" | ", _itens.SelectMany(i => i.Avisos).Distinct());
            RecalcularTotaisUi();
            MostrarMensagem($"Item '{atualizado.Descricao}' atualizado com os dados atuais do estoque.", sucesso: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ExceptionMessageHelper.ObterMensagemAmigavel(ex), "Atualizar item",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnRemoverItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ItemNotaFiscalDto item }) return;

        _itens.Remove(item);
        var numero = 1;
        foreach (var restante in _itens) restante.NItem = numero++;
        GridItens.Items.Refresh();
        RecalcularTotaisUi();
    }

    private void RecalcularTotaisUi()
    {
        if (_carregando || !_nota.PodeEditar) return;

        _nota.Itens = _itens.ToList();
        _nota.NumeroItens = _itens.Count;
        TxtNumeroItens.Text = _nota.NumeroItens.ToString();
        if (!_nota.CalculoAutomatico) return;

        _notaFiscalService.RecalcularTotais(_nota);
        AtualizarCamposCalculo(_nota);
    }

    // --- Bloco Cálculo do imposto ---

    private void AplicarPermissaoCalculo()
    {
        ChkCalculoAutomatico.IsEnabled = _nota.PodeEditar;
        foreach (var caixa in new[]
        {
            TxtVProd, TxtVFrete, TxtVSeg, TxtVBcIcms, TxtVIcms, TxtVBcIcmsSt, TxtVIcmsSt,
            TxtVIpi, TxtVIpiDevolvido, TxtVOutro, TxtVDesc, TxtVFunrural, TxtVAproxImp,
            TxtVFcp, TxtVFcpSt, TxtVFcpStRet
        })
        {
            caixa.IsReadOnly = !_nota.PodeEditar || ChkCalculoAutomatico.IsChecked == true;
        }
    }

    private void ChkCalculoAutomatico_Changed(object sender, RoutedEventArgs e)
    {
        if (_carregando || _atualizandoCalculo || !_nota.PodeEditar) return;

        try
        {
            // Preserva frete, seguro, despesas e desconto digitados ao voltar ao automático.
            if (!_nota.CalculoAutomatico)
                LerCamposCalculo();

            _nota.CalculoAutomatico = ChkCalculoAutomatico.IsChecked == true;
            AplicarPermissaoCalculo();
            RecalcularTotaisUi();
        }
        catch (DomainException ex)
        {
            _atualizandoCalculo = true;
            ChkCalculoAutomatico.IsChecked = false;
            _atualizandoCalculo = false;
            AplicarPermissaoCalculo();
            MostrarMensagem(ex.Message, sucesso: false);
        }
    }

    private void CampoCalculo_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_carregando || _atualizandoCalculo || _nota.CalculoAutomatico || !_nota.PodeEditar)
            return;
        if (e.OriginalSource == TxtVNf || e.OriginalSource == TxtNumeroItens) return;

        var caixasTotal = new[] { TxtVProd, TxtVIcmsSt, TxtVFrete, TxtVSeg, TxtVOutro, TxtVIpi, TxtVDesc };
        var valores = new decimal[caixasTotal.Length];
        for (var i = 0; i < caixasTotal.Length; i++)
            if (!TentarLerValorCalculo(caixasTotal[i], out valores[i])) return;

        TxtVNf.Text = (valores[0] + valores[1] + valores[2] + valores[3] + valores[4] + valores[5] - valores[6]).ToString("0.00");
    }

    private static bool TentarLerValorCalculo(TextBox caixa, out decimal valor)
    {
        valor = 0m;
        if (string.IsNullOrWhiteSpace(caixa.Text)) return true;
        if (!FormattingHelper.TryParseMoeda(caixa.Text, out valor) || valor < 0m) return false;
        valor = Math.Round(valor, 2, MidpointRounding.AwayFromZero);
        return true;
    }

    private void LerCamposCalculo()
    {
        var campos = new (TextBox Caixa, string Rotulo)[]
        {
            (TxtVProd, "Total Produtos"), (TxtVFrete, "Valor do Frete"), (TxtVSeg, "Valor do Seguro"),
            (TxtVBcIcms, "Base ICMS"), (TxtVIcms, "Valor ICMS"), (TxtVBcIcmsSt, "Base ICMS ST"),
            (TxtVIcmsSt, "Valor ICMS ST"), (TxtVIpi, "Valor IPI"), (TxtVIpiDevolvido, "IPI Devolvido"),
            (TxtVOutro, "Despesas"), (TxtVDesc, "Desconto"), (TxtVFunrural, "Valor Funrural"),
            (TxtVAproxImp, "Valor Aprox. Impostos"), (TxtVFcp, "Total do FCP"),
            (TxtVFcpSt, "Total FCP ST"), (TxtVFcpStRet, "FCP ST Ret. Anteriormente")
        };
        var valores = new decimal[campos.Length];
        for (var i = 0; i < campos.Length; i++)
        {
            if (!TentarLerValorCalculo(campos[i].Caixa, out var valor))
            {
                campos[i].Caixa.Focus();
                throw new DomainException($"Informe um valor válido, maior ou igual a zero, em {campos[i].Rotulo}.");
            }
            valores[i] = valor;
        }

        _nota.VProd = valores[0];
        _nota.VFrete = valores[1];
        _nota.VSeg = valores[2];
        _nota.VBcIcms = valores[3];
        _nota.VIcms = valores[4];
        _nota.VBcIcmsSt = valores[5];
        _nota.VIcmsSt = valores[6];
        _nota.VIpi = valores[7];
        _nota.VIpiDevolvido = valores[8];
        _nota.VOutro = valores[9];
        _nota.VDesc = valores[10];
        _nota.VFunrural = valores[11];
        _nota.VAproxImp = valores[12];
        _nota.VFcp = valores[13];
        _nota.VFcpSt = valores[14];
        _nota.VFcpStRet = valores[15];
        _nota.VNf = _nota.VProd + _nota.VIcmsSt + _nota.VFrete + _nota.VSeg + _nota.VOutro + _nota.VIpi - _nota.VDesc;
    }

    // --- Bloco Pagamento ---

    private void BtnAdicionarPagamento_Click(object sender, RoutedEventArgs e)
    {
        if (CmbNovaFormaPagamento.SelectedItem is not EnumItem<FormaPagamento> forma)
            return;

        // "Sem Pagamento" (nota sem venda de verdade por trás — transferência, amostra,
        // brinde) não tem valor a receber e não pode ser combinada com outra forma de
        // pagamento na mesma nota (NotaFiscalValidator garante isso na emissão) — aqui só
        // evita pedir um valor que não existe e já facilita substituindo qualquer pagamento
        // anterior, em vez de deixar o operador descobrir o erro só na hora de emitir.
        if (forma.Valor == FormaPagamento.SemPagamento)
        {
            _pagamentos.Clear();
            _pagamentos.Add(new PagamentoUi(new NotaFiscalPagamentoDto
            {
                FormaPagamento = forma.Valor,
                Valor = 0,
                QuantidadeParcelas = 1,
                Ordem = 0
            }));
            return;
        }

        if (!FormattingHelper.TryParseMoeda(TxtNovoPagamentoValor.Text, out var valor) || valor <= 0)
        {
            MessageBox.Show("Informe um valor válido para o pagamento.", "Pagamento", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(TxtNovoPagamentoParcelas.Text, out var parcelas) || parcelas < 1)
            parcelas = 1;

        // Se havia um "Sem Pagamento" sozinho, uma forma real substitui (não faz sentido
        // misturar) — mesma regra aplicada no caminho acima, agora no sentido inverso.
        // ObservableCollection<T> não tem RemoveAll, daí o ToList() antes de remover.
        foreach (var antigo in _pagamentos.Where(p => p.FormaPagamento == FormaPagamento.SemPagamento).ToList())
            _pagamentos.Remove(antigo);

        _pagamentos.Add(new PagamentoUi(new NotaFiscalPagamentoDto
        {
            FormaPagamento = forma.Valor,
            Valor = valor,
            QuantidadeParcelas = parcelas,
            Ordem = _pagamentos.Count
        }));

        TxtNovoPagamentoValor.Text = string.Empty;
        TxtNovoPagamentoParcelas.Text = "1";
    }

    private void BtnRemoverPagamento_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PagamentoUi item })
            _pagamentos.Remove(item);
    }

    // --- Rodapé ---

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void BtnSalvarRascunho_Click(object sender, RoutedEventArgs e)
    {
        // Esta tela só cria/edita o rascunho — a emissão de fato acontece em "Ações da
        // Nota" (separado de propósito, para que o horário de emissão enviado à SEFAZ
        // seja sempre o momento exato do clique em Emitir, não o momento em que a nota
        // foi digitada/salva — ver NotaFiscalService.EmitirAsync).
        BtnSalvarRascunho.IsEnabled = false;
        try
        {
            LerFormularioParaDto();
            _nota = _nota.Id == 0
                ? await _notaFiscalService.CriarRascunhoAsync(_nota)
                : await _notaFiscalService.AtualizarRascunhoAsync(_nota);
            NotaFiscalSalvaId = _nota.Id;

            // Quem abriu a tela a partir de uma venda já encadeia "Ações da Nota" na
            // sequência (ver VendaViewModel.EmitirNota) — repetir a instrução de procurar a
            // nota na lista só confundiria nesse caminho.
            if (!ChamadorAbreAcoesAposSalvar)
                MessageBox.Show(
                    "Nota salva como rascunho.\n\nPara emitir, abra \"Ações da Nota\" na lista e clique em Emitir.",
                    "Rascunho salvo", MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (DomainException ex)
        {
            MostrarMensagem(ex.Message, sucesso: false);
        }
        catch (Exception ex)
        {
            MostrarMensagem(ExceptionMessageHelper.ObterMensagemAmigavel(ex), sucesso: false);
        }
        finally
        {
            BtnSalvarRascunho.IsEnabled = true;
        }
    }

    private void MostrarMensagem(string mensagem, bool sucesso)
    {
        TxtMensagemStatus.Text = mensagem;
        TxtMensagemStatus.Foreground = (Brush)FindResource(sucesso ? "VerdeSucessoBrush" : "VermelhoErroBrush");
    }

    private void LerFormularioParaDto()
    {
        _nota.TipoSaida = TxtTipoSaida.Text;
        _nota.Serie = TxtSerie.Text.Trim();
        _nota.Numero = TxtNumero.Text.Trim();
        if (DateTime.TryParse(TxtDataSaida.Text, out var dataSaida)) _nota.DataSaida = dataSaida;
        _nota.Finalidade = ObterEnumSelecionado(CmbFinalidade, FinalidadeNfe.Normal);
        _nota.ConsumidorFinal = ChkConsumidorFinal.IsChecked == true;
        _nota.IndicadorPresenca = ObterEnumSelecionado(CmbIndicadorPresenca, IndicadorPresencaComprador.Presencial);
        _nota.IntermediadorCnpj = TextoOuNulo(TxtIntermediadorCnpj.Text);
        _nota.IntermediadorIdentificador = TextoOuNulo(TxtIntermediadorIdentificador.Text);
        var naturezaSelecionada = (CmbNatureza.SelectedItem as NaturezaItem)?.Origem;
        _nota.NaturezaOperacaoId = naturezaSelecionada?.Id;
        _nota.NaturezaOperacaoDescricao = naturezaSelecionada?.Descricao ?? string.Empty;

        _nota.DestinatarioTipoPessoa = RbPessoaJuridica.IsChecked == true ? TipoPessoa.Juridica : TipoPessoa.Fisica;
        _nota.DestinatarioNome = TextoOuNulo(TxtDestinatarioNome.Text);
        _nota.DestinatarioDocumento = SomenteDigitos(TxtDestinatarioDocumento.Text);
        _nota.DestinatarioIndicadorIe = (CmbIndicadorIe.SelectedItem as EnumItem<IndicadorIeDestinatario?>)?.Valor;
        _nota.DestinatarioInscricaoEstadual = TextoOuNulo(TxtDestinatarioIe.Text);
        _nota.Suframa = TextoOuNulo(TxtSuframa.Text);
        _nota.DestinatarioCep = SomenteDigitos(TxtDestinatarioCep.Text);
        _nota.DestinatarioLogradouro = TextoOuNulo(TxtDestinatarioLogradouro.Text);
        _nota.DestinatarioNumero = TextoOuNulo(TxtDestinatarioNumero.Text);
        _nota.DestinatarioComplemento = TextoOuNulo(TxtDestinatarioComplemento.Text);
        _nota.DestinatarioBairro = TextoOuNulo(TxtDestinatarioBairro.Text);
        _nota.DestinatarioCidade = TextoOuNulo(TxtDestinatarioCidade.Text);
        _nota.DestinatarioUf = TextoOuNulo(TxtDestinatarioUf.Text)?.ToUpperInvariant();
        _nota.DestinatarioCodigoMunicipioIbge = TextoOuNulo(TxtDestinatarioCodigoIbge.Text);
        _nota.DestinatarioTelefone = TextoOuNulo(TxtDestinatarioTelefone.Text);
        _nota.DestinatarioEmail = TextoOuNulo(TxtDestinatarioEmail.Text);
        _nota.Vendedor = TextoOuNulo(TxtVendedor.Text);
        _nota.ListaPrecoNome = TextoOuNulo(TxtListaPreco.Text);

        _nota.EntregaDiferenteCobranca = ChkEntregaDiferente.IsChecked == true;
        if (_nota.EntregaDiferenteCobranca)
        {
            _nota.EntregaCep = SomenteDigitos(TxtEntregaCep.Text);
            _nota.EntregaLogradouro = TextoOuNulo(TxtEntregaLogradouro.Text);
            _nota.EntregaNumero = TextoOuNulo(TxtEntregaNumero.Text);
            _nota.EntregaComplemento = TextoOuNulo(TxtEntregaComplemento.Text);
            _nota.EntregaBairro = TextoOuNulo(TxtEntregaBairro.Text);
            _nota.EntregaCidade = TextoOuNulo(TxtEntregaCidade.Text);
            _nota.EntregaUf = TextoOuNulo(TxtEntregaUf.Text);
        }

        _nota.Itens = _itens.ToList();
        _nota.Pagamentos = _pagamentos.Select(p => p.ParaDto()).ToList();

        _nota.CalculoAutomatico = ChkCalculoAutomatico.IsChecked == true;
        _nota.NumeroItens = _itens.Count;
        if (!_nota.CalculoAutomatico)
            LerCamposCalculo();
        _notaFiscalService.RecalcularTotais(_nota);
        if (_nota.VNf < 0m)
        {
            TxtVDesc.Focus();
            throw new DomainException("O desconto não pode deixar o total da nota negativo.");
        }
        AtualizarCamposCalculo(_nota);

        _nota.FormaEnvio = ObterEnumSelecionado(CmbFormaEnvio, ModalidadeFrete.SemOcorrenciaTransporte);
        FormattingHelper.TryParseQuantidade(TxtPesoBruto.Text, out var pesoBruto);
        _nota.PesoBruto = pesoBruto > 0 ? pesoBruto : null;
        FormattingHelper.TryParseQuantidade(TxtPesoLiquido.Text, out var pesoLiquido);
        _nota.PesoLiquido = pesoLiquido > 0 ? pesoLiquido : null;
        _nota.EnviarParaExpedicao = ChkEnviarExpedicao.IsChecked == true;

        _nota.FormaRecebimento = TextoOuNulo(TxtFormaRecebimento.Text);
        _nota.CategoriaFinanceira = TextoOuNulo(TxtCategoriaFinanceira.Text);
        _nota.CondicaoPagamento = TextoOuNulo(TxtCondicaoPagamento.Text);

        _nota.Deposito = TextoOuNulo(TxtDeposito.Text);
        _nota.Observacoes = TextoOuNulo(TxtObservacoes.Text);
        _nota.ObservacoesSistema = TextoOuNulo(TxtObservacoesSistema.Text);
        _nota.InformacoesFisco = TextoOuNulo(TxtInformacoesFisco.Text);
        _nota.Marcadores = TextoOuNulo(TxtMarcadores.Text);
    }

    // --- Helpers ---

    private static void SelecionarEnum<T>(ComboBox combo, T? valor) where T : struct, Enum
    {
        if (combo.ItemsSource is IEnumerable<EnumItem<T>> itens)
            combo.SelectedItem = itens.FirstOrDefault(i => Equals(i.Valor, valor)) ?? itens.FirstOrDefault();
        else if (combo.ItemsSource is IEnumerable<EnumItem<T?>> itensNullable)
            combo.SelectedItem = itensNullable.FirstOrDefault(i => Equals(i.Valor, valor)) ?? itensNullable.FirstOrDefault();
    }

    private static T ObterEnumSelecionado<T>(ComboBox combo, T padrao) where T : struct, Enum
        => (combo.SelectedItem as EnumItem<T>)?.Valor ?? padrao;

    private static string? TextoOuNulo(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static string SomenteDigitos(string? texto) => new((texto ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string DescricaoStatus(StatusNotaFiscal status) => status switch
    {
        StatusNotaFiscal.Rascunho => "RASCUNHO",
        StatusNotaFiscal.Emitindo => "EMITINDO",
        StatusNotaFiscal.Autorizada => "AUTORIZADA",
        StatusNotaFiscal.Rejeitada => "REJEITADA",
        StatusNotaFiscal.Cancelada => "CANCELADA",
        StatusNotaFiscal.Denegada => "DENEGADA",
        StatusNotaFiscal.Inutilizada => "INUTILIZADA",
        StatusNotaFiscal.Indeterminada => "INDETERMINADA",
        _ => status.ToString().ToUpperInvariant()
    };

    private static string DescricaoFinalidade(FinalidadeNfe f, string rotuloModelo) => f switch
    {
        FinalidadeNfe.Normal => $"{rotuloModelo} normal",
        FinalidadeNfe.Complementar => $"{rotuloModelo} complementar",
        FinalidadeNfe.Ajuste => $"{rotuloModelo} de ajuste",
        FinalidadeNfe.Devolucao => "Devolução de mercadoria",
        _ => f.ToString()
    };

    private static string DescricaoIndicadorPresenca(IndicadorPresencaComprador p) => p switch
    {
        IndicadorPresencaComprador.Presencial => "Operação presencial",
        IndicadorPresencaComprador.NaoPresencialInternet => "Não presencial, pela Internet",
        IndicadorPresencaComprador.NaoPresencialTeleatendimento => "Não presencial, Teleatendimento",
        IndicadorPresencaComprador.NfcePresencialEntregaDomicilio => "NFC-e com entrega a domicílio",
        IndicadorPresencaComprador.PresencialForaDoEstabelecimento => "Presencial, fora do estabelecimento",
        IndicadorPresencaComprador.NaoPresencialOutros => "Não presencial, outros",
        IndicadorPresencaComprador.NaoSeAplica => "Não se aplica",
        _ => p.ToString()
    };

    private static string DescricaoFrete(ModalidadeFrete f) => f switch
    {
        ModalidadeFrete.SemOcorrenciaTransporte => "Sem ocorrência de transporte",
        ModalidadeFrete.ContratacaoRemetenteCif => "Contratação do Frete por conta do Remetente (CIF)",
        ModalidadeFrete.ContratacaoDestinatarioFob => "Contratação do Frete por conta do Destinatário (FOB)",
        ModalidadeFrete.ContratacaoTerceiros => "Contratação do Frete por conta de Terceiros",
        ModalidadeFrete.TransporteProprioRemetente => "Transporte Próprio por conta do Remetente",
        ModalidadeFrete.TransporteProprioDestinatario => "Transporte Próprio por conta do Destinatário",
        _ => f.ToString()
    };

    private sealed record EnumItem<T>(T Valor, string Rotulo)
    {
        public override string ToString() => Rotulo;
    }

    /// <summary>
    /// Wrapper com <c>ToString()</c> para exibir a Natureza de Operação no ComboBox — o
    /// template customizado "ComboBoxModerno" não respeita <c>DisplayMemberPath</c> (o
    /// texto da caixa fechada acaba caindo no <c>ToString()</c> padrão do objeto inteiro),
    /// então o item exibido precisa já vir com o rótulo certo, mesmo padrão do <see cref="EnumItem{T}"/>.
    /// </summary>
    private sealed record NaturezaItem(NaturezaOperacaoDto? Origem, string Rotulo)
    {
        public override string ToString() => Rotulo;
    }

    private sealed class ClienteResultadoUi
    {
        public ClienteResultadoUi(ClienteDto origem) => Origem = origem;
        public ClienteDto Origem { get; }
        public override string ToString() => $"{Origem.Nome} — {Origem.DocumentoExibicao}";
    }

    private sealed class ProdutoResultadoUi
    {
        public ProdutoResultadoUi(ProdutoDto origem) => Origem = origem;
        public ProdutoDto Origem { get; }
        public override string ToString() => $"{Origem.CodigoInterno} — {Origem.NomeExibicao} ({FormattingHelper.FormatarMoeda(Origem.PrecoEfetivo)})";
    }

    private sealed class PagamentoUi
    {
        private readonly NotaFiscalPagamentoDto _dto;
        public PagamentoUi(NotaFiscalPagamentoDto dto) => _dto = dto;

        public FormaPagamento FormaPagamento => _dto.FormaPagamento;
        public string FormaPagamentoDescricao => _dto.FormaPagamento.ToString();
        public decimal Valor => _dto.Valor;
        public int QuantidadeParcelas => _dto.QuantidadeParcelas;

        public NotaFiscalPagamentoDto ParaDto() => _dto;
    }
}
