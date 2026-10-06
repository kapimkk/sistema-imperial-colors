using ImperialColors.UI.Helpers;

using ImperialColors.UI.Services;

using ImperialColors.UI.ViewModels;

using ImperialColors.Application.Interfaces;

using Microsoft.Extensions.DependencyInjection;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Threading;



namespace ImperialColors.UI.Views;



public partial class MainWindow : Window

{

    private readonly IServiceProvider _serviceProvider;

    private readonly IServiceScopeFactory _scopeFactory;

    private readonly ISessaoService _sessaoService;

    private readonly IAppConfigService _config;

    private readonly IBackupService _backupService;

    private readonly DispatcherTimer _relogio;

    private Button? _botaoMenuAtual;

    private IServiceScope? _escopoPagina;



    public MainWindow(IServiceProvider serviceProvider, IServiceScopeFactory scopeFactory,

        ISessaoService sessaoService, IAppConfigService config, IBackupService backupService)

    {

        InitializeComponent();

        _serviceProvider = serviceProvider;

        _scopeFactory = scopeFactory;

        _sessaoService = sessaoService;

        _config = config;

        _backupService = backupService;



        AplicarIdentidadeVisual();

        // A tela de Configurações grava o .env e recarrega a configuração em memória; sem este
        // gancho o menu lateral continuaria mostrando o nome antigo até o próximo reinício.
        _config.ConfiguracoesAlteradas += AoAlterarConfiguracoes;



        AplicarInformacoesUsuario();

        AplicarPermissoesMenu();



        _relogio = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        _relogio.Tick += (s, e) => TxtHora.Text = DateTime.Now.ToString("HH:mm:ss");

        _relogio.Start();



        NavigateToDashboard();

        _backupService.IniciarVerificacaoEmSegundoPlano();

        Closed += (_, _) =>
        {
            _config.ConfiguracoesAlteradas -= AoAlterarConfiguracoes;
            _escopoPagina?.Dispose();
        };
        PreviewKeyDown += JanelaPrincipal_PreviewKeyDown;
    }

    private void AoAlterarConfiguracoes(object? sender, EventArgs e)
        => UiDispatcher.ExecutarNaUi(AplicarIdentidadeVisual);

    private void AplicarIdentidadeVisual()
    {
        Title = $"{_config.EmpresaNome} - {_config.EmpresaSubtitulo}";
        TxtEmpresaNome.Text = _config.EmpresaNome;
        TxtEmpresaSubtitulo.Text = _config.EmpresaSubtitulo;
        LogoHelper.AplicarIconeJanela(this, _config.IconPath);
        LogoHelper.AplicarLogo(ImgLogoMenu, _config.LogoSemFundoPath, 44, 44);
    }

    private void JanelaPrincipal_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || Keyboard.Modifiers != ModifierKeys.None)
            return;

        e.Handled = true;
        AbrirPdvComFoco();
    }

    private void AbrirPdvComFoco()
    {
        using var escopo = _scopeFactory.CreateScope();
        if (escopo.ServiceProvider.GetRequiredService(typeof(PDVView)) is not PDVView pdv)
            return;

        pdv.PrepararFocoBusca();
        ModalWindowHelper.ExibirDialogo(pdv, this);
    }



    private IServiceProvider ObterServicosPagina()

    {

        _escopoPagina?.Dispose();

        _escopoPagina = _scopeFactory.CreateScope();

        return _escopoPagina.ServiceProvider;

    }



    private void AbrirModal<TWindow>() where TWindow : Window

    {

        using var escopo = _scopeFactory.CreateScope();

        if (escopo.ServiceProvider.GetRequiredService(typeof(TWindow)) is not TWindow janela)

            return;

        ModalWindowHelper.ExibirDialogo(janela, this);

    }



    private void DefinirMenuAtivo(Button botao)

    {

        if (_botaoMenuAtual is not null)

            NavMenuHelper.SetIsActive(_botaoMenuAtual, false);



        _botaoMenuAtual = botao;

        NavMenuHelper.SetIsActive(botao, true);

    }



    private void AplicarInformacoesUsuario()

    {

        var usuario = _sessaoService.UsuarioAtual;

        TxtUsuarioLogado.Text = usuario?.NomeCompleto ?? "—";

        TxtPermissaoLogada.Text = usuario?.Permissao switch

        {

            Domain.Enums.PermissaoUsuario.Admin => "Administrador",

            Domain.Enums.PermissaoUsuario.Caixa => "Caixa",

            _ => "—"

        };

    }



    private void AplicarPermissoesMenu()

    {

        BtnRelatorios.Visibility = _sessaoService.EhAdmin ? Visibility.Visible : Visibility.Collapsed;

    }



    private void NavigateToDashboard()

    {

        DefinirMenuAtivo(BtnDashboard);

        TxtTituloPagina.Text = "Dashboard";

        var vm = ObterServicosPagina().GetRequiredService<DashboardViewModel>();

        ConteudoPrincipal.Content = new DashboardView(vm, _config);

        _ = vm.CarregarDados();

    }



    private void BtnDashboard_Click(object sender, RoutedEventArgs e)

    {

        DefinirMenuAtivo(BtnDashboard);

        TxtTituloPagina.Text = "Dashboard";

        var vm = ObterServicosPagina().GetRequiredService<DashboardViewModel>();

        ConteudoPrincipal.Content = new DashboardView(vm, _config);

        _ = vm.CarregarDados();

    }



    private void BtnEstoque_Click(object sender, RoutedEventArgs e)

    {

        DefinirMenuAtivo(BtnEstoque);

        TxtTituloPagina.Text = "Controle de Estoque";

        var vm = ObterServicosPagina().GetRequiredService<ProdutoViewModel>();

        ConteudoPrincipal.Content = new EstoqueView(vm);

        _ = vm.CarregarAsync();

    }



    private void BtnVendas_Click(object sender, RoutedEventArgs e)

    {

        DefinirMenuAtivo(BtnVendas);

        TxtTituloPagina.Text = "Histórico de Vendas";

        var vm = ObterServicosPagina().GetRequiredService<VendaViewModel>();

        ConteudoPrincipal.Content = new VendasView(vm);

        _ = vm.CarregarAsync();

    }



    private void BtnVendasExternas_Click(object sender, RoutedEventArgs e)

    {

        DefinirMenuAtivo(BtnVendasExternas);

        TxtTituloPagina.Text = "Vendas Externas";

        var vm = ObterServicosPagina().GetRequiredService<VendaExternaViewModel>();

        ConteudoPrincipal.Content = new VendasExternasView(vm);

        _ = vm.CarregarAsync();

    }



    private void BtnVendasSite_Click(object sender, RoutedEventArgs e)

    {

        DefinirMenuAtivo(BtnVendasSite);

        TxtTituloPagina.Text = "Vendas do Site";

        var vm = ObterServicosPagina().GetRequiredService<VendasSiteViewModel>();

        ConteudoPrincipal.Content = new VendasSiteView(vm);

        _ = vm.CarregarAsync();

    }



    private void BtnPDV_Click(object sender, RoutedEventArgs e) => AbrirPdvComFoco();



    private void BtnOrcamentos_Click(object sender, RoutedEventArgs e)

    {

        DefinirMenuAtivo(BtnOrcamentos);

        TxtTituloPagina.Text = "Orçamentos";

        var vm = ObterServicosPagina().GetRequiredService<OrcamentoViewModel>();

        ConteudoPrincipal.Content = new OrcamentosView(vm);

        _ = vm.CarregarAsync();

    }



    private void BtnClientes_Click(object sender, RoutedEventArgs e)

    {

        DefinirMenuAtivo(BtnClientes);

        TxtTituloPagina.Text = "Clientes";

        var vm = ObterServicosPagina().GetRequiredService<ClienteViewModel>();

        ConteudoPrincipal.Content = new ClientesView(vm);

        _ = vm.CarregarAsync();

    }



    private void BtnMercadorias_Click(object sender, RoutedEventArgs e)

    {

        DefinirMenuAtivo(BtnMercadorias);

        TxtTituloPagina.Text = "Mercadorias";

        var fornecedorVm = ObterServicosPagina().GetRequiredService<FornecedorViewModel>();
        var listaVm = ObterServicosPagina().GetRequiredService<ListaCompraViewModel>();

        ConteudoPrincipal.Content = new MercadoriasView(fornecedorVm, listaVm);
        _ = fornecedorVm.CarregarAsync();
        _ = listaVm.CarregarAsync();

    }



    private void BtnNotaFiscal_Click(object sender, RoutedEventArgs e)

    {

        DefinirMenuAtivo(BtnNotaFiscal);

        TxtTituloPagina.Text = "Nota Fiscal";

        ConteudoPrincipal.Content = ObterServicosPagina().GetRequiredService<NotaFiscalHubView>();

    }



    private void BtnRelatorios_Click(object sender, RoutedEventArgs e)

    {

        if (!_sessaoService.EhAdmin)

        {

            MessageBox.Show("Acesso negado. Relatórios disponíveis apenas para administradores.",

                "Permissão insuficiente", MessageBoxButton.OK, MessageBoxImage.Warning);

            return;

        }



        DefinirMenuAtivo(BtnRelatorios);

        TxtTituloPagina.Text = "Relatórios";

        ConteudoPrincipal.Content = new RelatoriosView(_serviceProvider);

    }



    private void BtnConfiguracoes_Click(object sender, RoutedEventArgs e)

    {

        DefinirMenuAtivo(BtnConfiguracoes);

        TxtTituloPagina.Text = "Configurações";

        ConteudoPrincipal.Content = new ConfiguracoesView(_serviceProvider, _sessaoService);

    }



    private void BtnLogout_Click(object sender, RoutedEventArgs e)

    {

        if (MessageBox.Show("Deseja sair da sua conta?", "Confirmar logout",

                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)

            return;



        _sessaoService.EncerrarSessao();

        Hide();



        using var escopoLogin = _scopeFactory.CreateScope();

        var login = escopoLogin.ServiceProvider.GetRequiredService<LoginView>();

        if (ModalWindowHelper.ExibirDialogo(login, this) == true)

        {

            AplicarInformacoesUsuario();

            AplicarPermissoesMenu();

            NavigateToDashboard();

            WindowState = WindowState.Maximized;

            Show();

        }

        else

        {

            System.Windows.Application.Current.Shutdown();

        }

    }



    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)

    {

        if (e.ClickCount == 2)

            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        else

            DragMove();

    }



    private void BtnMinimizar_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximizar_Click(object sender, RoutedEventArgs e)

        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnFechar_Click(object sender, RoutedEventArgs e) => System.Windows.Application.Current.Shutdown();

}


