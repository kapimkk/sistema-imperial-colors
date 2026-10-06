using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ImperialColors.Application.Extensions;

public static class ApplicationExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IUsuarioAtual, UsuarioAtualSistema>();
        services.AddSingleton<IProdutoService, ProdutoService>();
        services.AddSingleton<ICategoriaService, CategoriaService>();
        services.AddSingleton<IMarcaService, MarcaService>();
        services.AddSingleton<IVendaService, VendaService>();
        services.AddSingleton<IClienteService, ClienteService>();
        services.AddSingleton<IFornecedorService, FornecedorService>();
        services.AddSingleton<IListaCompraService, ListaCompraService>();
        services.AddSingleton<ITrocaService, TrocaService>();
        services.AddSingleton<IVendaExternaService, VendaExternaService>();
        services.AddSingleton<IVendaSiteService, VendaSiteService>();
        services.AddSingleton<IOrcamentoService, OrcamentoService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IRelatorioAnalyticsService, RelatorioAnalyticsService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IUsuarioService, UsuarioService>();
        services.AddSingleton<IAuditoriaService, AuditoriaService>();
        services.AddSingleton<IConfiguracaoFiscalService, ConfiguracaoFiscalService>();
        services.AddSingleton<ICalculoFiscalVendaService, CalculoFiscalVendaService>();
        services.AddSingleton<INaturezaOperacaoService, NaturezaOperacaoService>();
        services.AddSingleton<INotaFiscalService, NotaFiscalService>();
        return services;
    }
}
