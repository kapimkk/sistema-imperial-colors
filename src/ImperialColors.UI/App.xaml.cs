using DotNetEnv;
using ImperialColors.Domain.Helpers;
using ImperialColors.Application.Configuration;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Contingency;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Extensions;
using ImperialColors.UI.Helpers;
using ImperialColors.UI.Services;
using ImperialColors.UI.ViewModels;
using ImperialColors.UI.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO;
using System.Windows;

namespace ImperialColors.UI;

public partial class App : System.Windows.Application
{

    private IHost? _host;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        ConfigurarCulturaPtBr();
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Erro inesperado:\n\n{args.Exception.Message}",
                "Erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Erro crítico:\n\n{ex.Message}",
                    "Erro",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            MessageBox.Show(
                $"Erro em operação assíncrona:\n\n{args.Exception.Message}",
                "Erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.SetObserved();
        };

        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        CarregarArquivoEnv();

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<EmpresaConfig>(ctx.Configuration.GetSection(EmpresaConfig.Secao));
                services.PostConfigure<EmpresaConfig>(EmpresaConfigEnvironmentOverrides.Aplicar);

                services.Configure<FiscalApiConfig>(ctx.Configuration.GetSection(FiscalApiConfig.Secao));
                services.PostConfigure<FiscalApiConfig>(FiscalApiConfigEnvironmentOverrides.Aplicar);

                var connectionString = AppConfigService.MontarConnectionString();
                services.AddInfrastructure(connectionString);
                services.AddApplication();

                services.AddSingleton<IAppConfigService, AppConfigService>();
                services.AddSingleton<SessaoService>();
                services.AddSingleton<ISessaoService>(sp => sp.GetRequiredService<SessaoService>());
                services.AddSingleton<IUsuarioAtual>(sp => sp.GetRequiredService<SessaoService>());
                services.AddSingleton<IRelatorioService, RelatorioService>();
                services.AddSingleton<IArquivoEnvService, ArquivoEnvService>();
                services.AddSingleton<IConfiguracoesAplicacaoService, ConfiguracoesAplicacaoService>();
                services.AddSingleton<DocumentosPdfService>();

                services.AddTransient<LoginViewModel>();
                services.AddTransient<GestaoUsuariosViewModel>();
                services.AddTransient<AuditoriaLogsViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProdutoViewModel>();
                services.AddTransient<VendaViewModel>();
                services.AddTransient<ClienteViewModel>();
                services.AddTransient<FornecedorViewModel>();
                services.AddTransient<ListaCompraViewModel>();
                services.AddTransient<VendaExternaViewModel>();
                services.AddTransient<VendasSiteViewModel>();
                services.AddTransient<OrcamentoViewModel>();
                services.AddTransient<NaturezaOperacaoViewModel>();

                services.AddTransient<LoginView>();
                services.AddTransient<ImperialColors.UI.Views.MainWindow>();
                services.AddTransient<PDVView>();
                services.AddTransient<ProdutoFormView>();
                services.AddTransient<MovimentacaoEstoqueView>();
                services.AddTransient<ClienteFormView>();
                services.AddTransient<FornecedorFormView>();
                services.AddTransient<ListaCompraFormView>();
                services.AddTransient<TrocaFormView>();
                services.AddTransient<EditarVendaView>();
                services.AddTransient<VendaExternaFormView>();
                services.AddTransient<ComissoesVendaExternaView>();
                services.AddTransient<OrcamentoFormView>();
                services.AddTransient<CupomView>();
                services.AddTransient<GestaoUsuariosView>();
                services.AddTransient<AuditoriaLogsView>();
                services.AddTransient<PerifericosView>();
                services.AddTransient<FiscalConfigView>();
                services.AddTransient<NaturezaOperacaoView>();
                services.AddTransient<NotaFiscalHubView>();
                services.AddTransient<AtualizacaoSistemaDialogView>();

                services.AddLogging(logging =>
                {
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Information);
                });
            })
            .Build();

        await _host.StartAsync();

        try
        {
            using var scope = _host.Services.CreateScope();
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var dbContext = await contextFactory.CreateDbContextAsync();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<App>>();
            await dbContext.Database.MigrateAsync();
            await UsuarioDatabaseSeeder.SeedAdminAsync(dbContext, logger);

            // A retenção de logs de auditoria saiu daqui: rodar só na abertura significava,
            // num PDV que fica aberto a semana inteira, quase nunca rodar. Agora é
            // DataSyncService.ExpurgarLogsSeVencidoAsync, que dispara no primeiro tick do laço
            // (mantendo o efeito de "expurga ao abrir") e depois a cada 24h.

            var contingencyFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ContingencyDbContext>>();
            await using var contingencyDb = await contingencyFactory.CreateDbContextAsync();
            await contingencyDb.Database.EnsureCreatedAsync();
        }
        catch (Exception ex)
        {
            var resultado = MessageBox.Show(
                $"Erro ao conectar com o banco de dados:\n\n{ex.Message}\n\n" +
                "Verifique o arquivo .env e tente novamente.\n\nDeseja continuar mesmo assim?",
                "Erro de Banco de Dados",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (resultado == MessageBoxResult.No)
            {
                Shutdown();
                return;
            }
        }

        // Trava de coordenação entre PDVs: o banco é compartilhado, mas cada caixa roda sua
        // própria cópia dos arquivos. Se outro caixa já atualizou e abriu primeiro, ele já
        // aplicou migrations NO BANCO — este caixa, se ainda estiver na versão antiga, pode
        // estar rodando um modelo do EF que não conhece mais a estrutura real da tabela.
        //
        // Fica em bloco próprio, separado do try/catch de conexão acima: uma falha aqui é
        // um problema de coordenação (avisar o operador), não um problema de banco fora do
        // ar — não faz sentido reaproveitar a mensagem "Erro de Banco de Dados" para isso, e
        // muito menos travar a abertura do sistema por causa de um aviso que falhou.
        try
        {
            using var scope = _host.Services.CreateScope();
            var coordenacao = scope.ServiceProvider.GetRequiredService<ICoordenacaoAtualizacaoBancoService>();
            var resultadoCoordenacao = await coordenacao.VerificarERegistrarAsync();

            if (resultadoCoordenacao.BancoAtualizadoPorOutraInstalacaoMaisNova)
            {
                var resposta = MessageBox.Show(
                    $"Este caixa está na versão {resultadoCoordenacao.VersaoInstaladaTexto}, mas o caixa " +
                    $"'{resultadoCoordenacao.MaquinaQueAtualizouPorUltimo}' já abriu este sistema na versão " +
                    $"{resultadoCoordenacao.VersaoRegistradaTexto}.\n\n" +
                    "A estrutura do banco de dados pode já ter mudado. Continuar numa versão mais antiga pode " +
                    "causar erros ao gravar vendas ou notas fiscais.\n\n" +
                    "Atualize este caixa (Configurações → Geral → Atualizar Sistema) antes de continuar vendendo.\n\n" +
                    "Deseja continuar mesmo assim?",
                    "Este caixa está desatualizado",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (resposta == MessageBoxResult.No)
                {
                    Shutdown();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogWarning(ex, "Não foi possível verificar a coordenação de versão entre PDVs.");
        }

        try
        {
            var loginView = _host.Services.GetRequiredService<LoginView>();
            if (loginView.ShowDialog() != true)
            {
                Shutdown();
                return;
            }

            var mainWindow = _host.Services.GetRequiredService<ImperialColors.UI.Views.MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Closed += (_, _) => Shutdown();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível iniciar o sistema após o login:\n\n{ex.Message}",
                "Erro ao Iniciar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override async void OnExit(System.Windows.ExitEventArgs e)
    {
        _host?.Services.GetService<ISessaoService>()?.EncerrarSessao();

        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }

    internal static void ConfigurarCulturaPtBr()
        => FormattingHelper.ConfigurarCulturaAplicacao();

    private static void CarregarArquivoEnv()
    {
        if (ArquivoEnvLocalizador.LocalizarExistente() is { } caminho)
            Env.Load(caminho);
    }
}
