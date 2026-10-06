using System.IO;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Infrastructure.Configuration;
using ImperialColors.Infrastructure.Extensions;
using ImperialColors.Infrastructure.Services;
using ImperialColors.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// Validação ponta a ponta da integração: a MESMA rotina que o botão "Sincronizar com o Site"
/// aciona, com o <b>ImperialSync.exe de verdade</b>, a API do site rodando e o PostgreSQL local — e,
/// ao final, a venda do site aparecendo na aba "Vendas Site".
///
/// Não é um teste de rotina: depende de um ambiente montado à mão (API local no ar, um pedido PAGO
/// esperando na fila, ImperialSync.exe com o ImperialSync.env ao lado). Por isso só roda quando as
/// variáveis abaixo existem; sem elas termina sem fazer nada, como os testes de integração com o
/// banco fazem sem <c>RUN_INTEGRATION_TESTS</c>.
///
/// <list type="bullet">
/// <item><c>IMPERIALSYNC_E2E_EXE</c> — caminho do ImperialSync.exe (com o ImperialSync.env ao lado);</item>
/// <item><c>IMPERIALSYNC_E2E_CONEXAO</c> — string de conexão do banco da loja que o SISTEMA usa;</item>
/// <item><c>IMPERIALSYNC_E2E_PEDIDOS</c> — números dos pedidos do site à espera, separados por vírgula
/// (os que a primeira sincronização deve transformar em venda).</item>
/// </list>
/// </summary>
public class SincronizacaoSiteExeRealTests
{
    public SincronizacaoSiteExeRealTests() => WpfTestBootstrap.Inicializar();

    private static bool Configurado(out string exe, out string conexao, out string[] pedidos)
    {
        exe = Environment.GetEnvironmentVariable("IMPERIALSYNC_E2E_EXE") ?? string.Empty;
        conexao = Environment.GetEnvironmentVariable("IMPERIALSYNC_E2E_CONEXAO") ?? string.Empty;
        pedidos = (Environment.GetEnvironmentVariable("IMPERIALSYNC_E2E_PEDIDOS") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return File.Exists(exe) && conexao.Length > 0 && pedidos.Length > 0;
    }

    [WpfFact]
    public async Task Botao_ComOExeReal_CriaAsVendasEElasAparecemEmVendasSite_SemDuplicarNaSegundaVez()
    {
        if (!Configurado(out var exe, out var conexao, out var pedidos))
            return;

        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(conexao);
        services.AddApplication();
        // O mesmo serviço do sistema, só apontando para o executável do ambiente de teste.
        services.AddSingleton<ISincronizacaoSiteService>(new SincronizacaoSiteService(
            new SincronizacaoSiteOptions { CaminhoExecutavel = exe, TempoMaximo = TimeSpan.FromMinutes(3) }));

        await using var provider = services.BuildServiceProvider();
        var vm = new VendasSiteViewModel(
            provider.GetRequiredService<IVendaSiteService>(),
            provider.GetRequiredService<ISincronizacaoSiteService>());

        // 1. Antes: os pedidos ainda não são vendas na loja.
        await vm.CarregarAsync();
        var totalAntes = vm.TotalItens;
        Assert.DoesNotContain(vm.Vendas, v => pedidos.Contains(v.PedidoSite));

        // 2. O botão: roda o ImperialSync.exe --once de verdade e espera terminar.
        await vm.SincronizarAsync();

        Assert.False(vm.Sincronizando);
        Assert.True(
            vm.GravidadeSincronizacao == GravidadeSincronizacao.Sucesso,
            $"a sincronização não terminou bem: {vm.MensagemSincronizacao}\n{vm.DetalhesSincronizacao}");
        Assert.Equal("Sincronização concluída com sucesso.", vm.MensagemSincronizacao);
        Assert.Contains("código de saída 0", vm.RodapeSincronizacao);
        Assert.Contains("criada(s) na loja", vm.DetalhesSincronizacao);

        // 3. A tela já foi recarregada sozinha e mostra as vendas novas, completas.
        Assert.Equal(totalAntes + pedidos.Length, vm.TotalItens);
        foreach (var pedido in pedidos)
        {
            var venda = Assert.Single(vm.Vendas, v => v.PedidoSite == pedido);
            Assert.True(venda.VendaExiste);
            Assert.Equal("Finalizada", venda.StatusDescricao);
            Assert.Matches(@"^\d{8}-\d{4}$", venda.NumeroVenda);
            Assert.True(venda.Total > 0m);
            Assert.NotEqual("—", venda.Pagamento);
            Assert.True((DateTime.Now - venda.SincronizadoEm).TotalMinutes < 10);
        }

        // 4. Nada de segredo, conexão ou documento na tela.
        Assert.DoesNotContain("Password", vm.DetalhesSincronizacao, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", vm.DetalhesSincronizacao);
        Assert.DoesNotMatch("[0-9a-fA-F]{32,}", vm.DetalhesSincronizacao);

        // 5. Segunda vez: o botão roda de novo e NADA é duplicado.
        var vendasAntesDaSegunda = vm.Vendas.Select(v => v.NumeroVenda).ToList();
        await vm.SincronizarAsync();

        Assert.Equal(GravidadeSincronizacao.Sucesso, vm.GravidadeSincronizacao);
        Assert.Equal(totalAntes + pedidos.Length, vm.TotalItens);
        Assert.Equal(vendasAntesDaSegunda, vm.Vendas.Select(v => v.NumeroVenda).ToList());
        Assert.Contains("nenhuma venda pendente", vm.DetalhesSincronizacao);
    }
}
