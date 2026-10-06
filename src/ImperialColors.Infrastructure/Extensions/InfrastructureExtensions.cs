using ImperialColors.Application.Configuration;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Configuration;
using ImperialColors.Infrastructure.Atualizacao;
using ImperialColors.Infrastructure.Contingency;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Fiscal;
using ImperialColors.Infrastructure.Repositories;
using ImperialColors.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ImperialColors.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // Pooled: o padrão do projeto é abrir um DbContext por método de repositório, então
        // uma única tela dispara dezenas de instanciações. O pool reaproveita os objetos
        // internos (model, change tracker, conexão), tirando esse custo do caminho quente do
        // PDV. Sem a versão pooled, cada CreateDbContext reconstruía tudo do zero.
        //
        // CommandTimeout explícito: deixa o limite declarado no código em vez de depender do
        // padrão do provider, para uma consulta pesada não travar a interface indefinidamente.
        //
        // EnableRetryOnFailure: uma oscilação momentânea de rede deixa de derrubar a venda.
        // Antes, uma falha transitória empurrava a operação para o caminho de contingência
        // (SQLite local + sincronização posterior) — que funciona, mas é bem mais pesado que
        // simplesmente repetir depois de um segundo.
        //
        // Só é seguro ligar porque TODAS as 11 transações manuais do sistema passam por
        // RepositoryBase.ExecutarEmTransacaoAsync, que as executa sob
        // Database.CreateExecutionStrategy() e cria um DbContext novo a cada tentativa. Sem
        // isso, o EF lança InvalidOperationException ("does not support user-initiated
        // transactions") no primeiro SaveChanges dentro de um BeginTransaction manual — e o
        // detalhe traiçoeiro é que o BeginTransaction sozinho NÃO lança, então o erro só
        // apareceria na primeira venda real do cliente.
        //
        // ⚠️ Ao adicionar uma transação nova, use ExecutarEmTransacaoAsync. Um
        // BeginTransactionAsync solto volta a quebrar em produção passando pelos testes.
        //
        // A estratégia do Npgsql só repete erros classificados como transitórios e nunca
        // reexecuta uma transação já confirmada — não há risco de venda duplicada.
        services.AddPooledDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                npgsql.CommandTimeout(30);
            }));

        var caminhoSqlite = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImperialColors",
            "pdv_contingency.db");
        Directory.CreateDirectory(Path.GetDirectoryName(caminhoSqlite)!);

        services.AddDbContextFactory<ContingencyDbContext>(options =>
            options.UseSqlite($"Data Source={caminhoSqlite}"));

        // Func<BackupOptions>, não uma instância: BACKUP_PATH pode mudar em tempo real pela
        // tela de Configurações (ArquivoEnvService), e uma instância calculada uma vez no
        // boot nunca veria essa troca até reiniciar o processo — achado da auditoria de
        // 15/09. BackupService chama isto a cada verificação, não guarda o resultado.
        services.AddSingleton<Func<BackupOptions>>(_ => BackupOptions.CarregarDoAmbiente);
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IParametroSistemaRepository, ParametroSistemaRepository>();
        services.AddSingleton<ICoordenacaoAtualizacaoBancoService, CoordenacaoAtualizacaoBancoService>();

        services.AddSingleton<IProdutoRepository, ProdutoRepository>();
        services.AddSingleton<IVendaRepository, VendaRepository>();
        services.AddSingleton<IClienteRepository, ClienteRepository>();
        services.AddSingleton<IFornecedorRepository, FornecedorRepository>();
        services.AddSingleton<IListaCompraRepository, ListaCompraRepository>();
        services.AddSingleton<IMovimentacaoEstoqueRepository, MovimentacaoEstoqueRepository>();
        services.AddSingleton<IRepository<Categoria>, CategoriaRepository>();
        services.AddSingleton<IRepository<Marca>, MarcaRepository>();
        services.AddSingleton<IRepository<ListaCompra>, ListaCompraRepository>();
        services.AddSingleton<IRepository<ItemListaCompra>, RepositoryBase<ItemListaCompra>>();
        services.AddSingleton<IUsuarioRepository, UsuarioRepository>();
        services.AddSingleton<ITrocaRepository, TrocaRepository>();
        services.AddSingleton<IVendaExternaRepository, VendaExternaRepository>();
        services.AddSingleton<IVendaSiteRepository, VendaSiteRepository>();
        services.AddSingleton<IOrcamentoRepository, OrcamentoRepository>();
        services.AddSingleton<IRelatorioAnalyticsRepository, RelatorioAnalyticsRepository>();
        services.AddSingleton<ILogAuditoriaRepository, LogAuditoriaRepository>();
        services.AddSingleton<ITributacaoProdutoRepository, TributacaoProdutoRepository>();
        services.AddSingleton<ITributacaoCategoriaRepository, TributacaoCategoriaRepository>();
        services.AddSingleton<IConfiguracaoFiscalEmpresaRepository, ConfiguracaoFiscalEmpresaRepository>();
        services.AddSingleton<IRepository<NaturezaOperacao>, RepositoryBase<NaturezaOperacao>>();
        services.AddSingleton<INotaFiscalRepository, NotaFiscalRepository>();

        services.AddSingleton<IContingencyVendaService, ContingencyVendaService>();

        // Executa o ImperialSync.exe (que fica ao lado do executável do sistema) e interpreta o
        // resultado. Singleton: a trava de "uma sincronização por vez" vale para todas as telas.
        services.AddSingleton<ISincronizacaoSiteService>(sp => new SincronizacaoSiteService(
            new SincronizacaoSiteOptions(),
            sp.GetService<ILogger<SincronizacaoSiteService>>()));

        services.AddSingleton<DatabaseHealthService>();
        services.AddSingleton<IDatabaseHealthService>(sp => sp.GetRequiredService<DatabaseHealthService>());
        services.AddHostedService(sp => sp.GetRequiredService<DatabaseHealthService>());

        services.AddSingleton<DataSyncService>();
        services.AddSingleton<IDataSyncService>(sp => sp.GetRequiredService<DataSyncService>());
        services.AddHostedService(sp => sp.GetRequiredService<DataSyncService>());

        services.AddSingleton<IPrinterService, PrinterService>();
        services.AddSingleton<ILocalConfigService, LocalConfigService>();

        services.AddHttpClient<IViaCepService, ViaCepService>(client =>
        {
            client.BaseAddress = new Uri("https://viacep.com.br/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ImperialColors/1.0");
        });

        services.AddHttpClient<ReceitaWsCnpjService>(client =>
        {
            client.BaseAddress = new Uri("https://receitaws.com.br/");
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ImperialColors/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddHttpClient<BrasilApiCnpjService>(client =>
        {
            client.BaseAddress = new Uri("https://brasilapi.com.br/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ImperialColors/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddTransient<ICnpjConsultaService, CnpjConsultaCompostaService>();

        services.AddHttpClient<INcmService, BrasilApiNcmService>(client =>
        {
            client.BaseAddress = new Uri("https://brasilapi.com.br/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ImperialColors/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        // API Fiscal (PFCode) — dois HttpClients nomeados (NF-e :5001 / NFC-e :5002),
        // ver comentário em FiscalApiClient sobre por que não é o AddHttpClient<T> típico.
        services.AddHttpClient(FiscalApiClient.ClienteNFe, (sp, client) =>
        {
            var config = sp.GetRequiredService<IOptions<FiscalApiConfig>>().Value;
            client.BaseAddress = new Uri(config.NFeBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient(FiscalApiClient.ClienteNFCe, (sp, client) =>
        {
            var config = sp.GetRequiredService<IOptions<FiscalApiConfig>>().Value;
            client.BaseAddress = new Uri(config.NFCeBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddTransient<IFiscalApiClient, FiscalApiClient>();

        // Qual repositório do GitHub fornece as releases — lido uma vez na subida, do .env.
        // Fica registrado como instância (e não Func<>, como BackupOptions): a pasta de
        // backup muda pela tela de Configurações em tempo real, o repositório de atualização
        // não tem tela e só muda quando alguém edita o .env e reabre o sistema.
        services.AddSingleton(AtualizacaoConfig.CarregarDoAmbiente());

        // Atualização do sistema pelas Releases do GitHub. Timeout longo porque o mesmo
        // cliente faz a consulta (rápida) e o download do pacote self-contained, que passa
        // de 100 MB — 30 segundos derrubariam o download em qualquer conexão de loja.
        services.AddHttpClient<IAtualizadorSistemaService, AtualizadorSistemaService>(client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.Timeout = TimeSpan.FromMinutes(15);
            // A API do GitHub recusa requisição sem User-Agent com 403.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ImperialColors-Atualizador");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        });

        return services;
    }
}
