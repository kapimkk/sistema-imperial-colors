using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Extensions;
using ImperialColors.Infrastructure.Repositories;
using ImperialColors.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Os serviços da aba "Vendas Site" como o container do sistema (<c>AddInfrastructure</c> +
/// <c>AddApplication</c>) os entrega. Não abre conexão: só confere o registro.
/// </summary>
public class RegistroDeServicosVendasSiteTests
{
    private static ServiceProvider CriarContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Nada conecta aqui: o provedor só guarda a string até a primeira consulta.
        services.AddInfrastructure("Host=localhost;Port=1;Database=teste;Username=teste;Password=teste");
        services.AddApplication();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ServicoQueExecutaOImperialSync_EhUnicoNoSistema_ENasceApontandoParaAPastaDoExecutavel()
    {
        using var container = CriarContainer();

        var primeiro = container.GetRequiredService<ISincronizacaoSiteService>();
        var segundo = container.GetRequiredService<ISincronizacaoSiteService>();

        // Singleton: a trava de "uma sincronização por vez" é do objeto, então vale para TODAS as telas.
        Assert.Same(primeiro, segundo);
        Assert.IsType<SincronizacaoSiteService>(primeiro);
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "ImperialSync.exe"), primeiro.CaminhoExecutavel);
        Assert.False(primeiro.EmExecucao);
        Assert.Null(primeiro.UltimoResultado);
    }

    [Fact]
    public void LeituraDasVendasDoSite_EstaRegistradaDoRepositorioAoServico()
    {
        using var container = CriarContainer();

        var servico = container.GetRequiredService<IVendaSiteService>();
        var repositorio = container.GetRequiredService<IVendaSiteRepository>();

        Assert.IsType<VendaSiteService>(servico);
        Assert.IsType<VendaSiteRepository>(repositorio);
        Assert.Same(servico, container.GetRequiredService<IVendaSiteService>());
        Assert.Same(repositorio, container.GetRequiredService<IVendaSiteRepository>());
    }
}
