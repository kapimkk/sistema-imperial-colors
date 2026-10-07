using System.IO;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.ReadModels;
using ImperialColors.Infrastructure.Configuration;
using ImperialColors.Infrastructure.Extensions;
using ImperialColors.Infrastructure.Services;
using ImperialColors.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
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
/// Rode esta classe SOZINHA (<c>--filter FullyQualifiedName~SincronizacaoSiteExeRealTests</c>). Estes
/// testes esperam um processo de verdade por cerca de um segundo; junto com outras classes de teste
/// de tela, que o xUnit roda em paralelo e que dividem o mesmo <c>Application</c> do WPF, uma delas
/// pode terminar no meio e derrubar a chamada da outra (<c>TaskCanceledException</c> no Dispatcher).
///
/// <list type="bullet">
/// <item><c>IMPERIALSYNC_E2E_EXE</c> — caminho do ImperialSync.exe (com o ImperialSync.env ao lado);</item>
/// <item><c>IMPERIALSYNC_E2E_CATALOGO</c> — o que o catálogo do site tem dos produtos da loja:
/// <c>completo</c> (todos), <c>parcial</c> (alguns) ou <c>vazio</c> (nenhum). É o que decide a cor da
/// faixa: verde só com <c>completo</c>; nos outros dois, alerta;</item>
/// <item><c>IMPERIALSYNC_E2E_CONEXAO</c> — string de conexão do banco da loja que o SISTEMA usa
/// (só o teste das vendas precisa);</item>
/// <item><c>IMPERIALSYNC_E2E_PEDIDOS</c> — números dos pedidos do site à espera, separados por vírgula
/// (os que a primeira sincronização deve transformar em venda; só o teste das vendas precisa).</item>
/// </list>
/// </summary>
public class SincronizacaoSiteExeRealTests
{
    public SincronizacaoSiteExeRealTests() => WpfTestBootstrap.Inicializar();

    private static string Variavel(string nome) => (Environment.GetEnvironmentVariable(nome) ?? string.Empty).Trim();

    private static bool Configurado(out string exe, out string conexao, out string[] pedidos)
    {
        exe = Variavel("IMPERIALSYNC_E2E_EXE");
        conexao = Variavel("IMPERIALSYNC_E2E_CONEXAO");
        pedidos = Variavel("IMPERIALSYNC_E2E_PEDIDOS")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return File.Exists(exe) && conexao.Length > 0 && pedidos.Length > 0;
    }

    /// <summary>O catálogo do site declarado pelo ambiente; vazio quando a variável não existe.</summary>
    private static string Catalogo() => Variavel("IMPERIALSYNC_E2E_CATALOGO").ToLowerInvariant();

    /// <summary>
    /// O que a faixa precisa mostrar depois de uma rodada com o exe real, conforme o catálogo do site.
    /// "A API aceitou" não basta para o verde: ele só vale com TODOS os produtos reconhecidos.
    /// </summary>
    private static void ConferirFaixaDoEstoque(VendasSiteViewModel vm, string catalogo)
    {
        var contexto = $"{vm.MensagemSincronizacao}\n{vm.ResumoEstoque}\n{vm.RodapeSincronizacao}\n{vm.DetalhesSincronizacao}";
        Assert.True(vm.TemResumoEstoque, $"o resumo do estoque não foi lido da saída do programa:\n{contexto}");
        Assert.Matches(@"^Recebidos pelo site: [\d.]+  ·  Atualizados: [\d.]+  ·  SKUs sem cadastro: [\d.]+$", vm.ResumoEstoque);

        switch (catalogo)
        {
            case "completo":
                Assert.True(vm.GravidadeSincronizacao == GravidadeSincronizacao.Sucesso, contexto);
                Assert.Equal("Sincronização concluída com sucesso.", vm.MensagemSincronizacao);
                Assert.Contains("código de saída 0", vm.RodapeSincronizacao);
                Assert.EndsWith("SKUs sem cadastro: 0", vm.ResumoEstoque);
                Assert.False(vm.TemAmostraSemCadastro);
                Assert.False(vm.TemOrientacaoEstoque);
                break;

            case "parcial":
                Assert.True(vm.GravidadeSincronizacao == GravidadeSincronizacao.Atencao, contexto);
                Assert.Matches(
                    @"^Sincronização concluída com alerta\. [\d.]+ dos [\d.]+ produtos enviados não têm? cadastro no catálogo do site\.$",
                    vm.MensagemSincronizacao);
                Assert.Contains("código de saída 15", vm.RodapeSincronizacao);
                Assert.DoesNotMatch(@"Atualizados: 0  ·", vm.ResumoEstoque);
                Assert.True(vm.TemAmostraSemCadastro);
                Assert.True(vm.TemOrientacaoEstoque);
                break;

            case "vazio":
                Assert.True(vm.GravidadeSincronizacao == GravidadeSincronizacao.Atencao, contexto);
                Assert.Matches(
                    @"^Sincronização concluída com alerta\. [\d.]+ produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site\.$",
                    vm.MensagemSincronizacao);
                Assert.Contains("código de saída 14", vm.RodapeSincronizacao);
                Assert.Contains("Atualizados: 0  ·", vm.ResumoEstoque);
                Assert.True(vm.TemAmostraSemCadastro);
                Assert.True(vm.TemOrientacaoEstoque);
                break;

            default:
                Assert.Fail($"IMPERIALSYNC_E2E_CATALOGO precisa ser 'completo', 'parcial' ou 'vazio' (veio '{catalogo}').");
                break;
        }

        // Em nenhum dos três casos é falha técnica: a comunicação funcionou.
        Assert.NotEqual(GravidadeSincronizacao.Erro, vm.GravidadeSincronizacao);
        Assert.DoesNotContain("NÃO concluída", vm.DetalhesSincronizacao);
    }

    private static void ConferirQueNadaSecretoApareceu(VendasSiteViewModel vm)
    {
        foreach (var texto in new[] { vm.DetalhesSincronizacao, vm.MensagemSincronizacao, vm.ResumoEstoque, vm.AmostraSemCadastro })
        {
            Assert.DoesNotContain("Password", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Host=", texto);
            Assert.DoesNotMatch("[0-9a-fA-F]{32,}", texto);
        }
    }

    [WpfFact]
    public async Task Botao_ComOExeReal_ClassificaOEstoquePeloCatalogoDoSite()
    {
        var exe = Variavel("IMPERIALSYNC_E2E_EXE");
        var catalogo = Catalogo();
        if (!File.Exists(exe) || catalogo.Length == 0)
            return;

        // Só o estoque interessa aqui: a lista de vendas é de mentira, o ImperialSync.exe é o de verdade.
        var vendas = new Mock<IVendaSiteService>();
        vendas
            .Setup(s => s.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoVendasSiteDto
            {
                Pagina = new PaginacaoResultadoDto<VendaSiteDto> { Itens = [], PaginaAtual = 1, ItensPorPagina = 50, TotalItens = 0 }
            });
        var servico = new SincronizacaoSiteService(
            new SincronizacaoSiteOptions { CaminhoExecutavel = exe, TempoMaximo = TimeSpan.FromMinutes(3) });
        var vm = new VendasSiteViewModel(vendas.Object, servico);

        await vm.SincronizarAsync();

        Assert.False(vm.Sincronizando);
        ConferirFaixaDoEstoque(vm, catalogo);
        ConferirQueNadaSecretoApareceu(vm);
        // O que a tela mostra é o que o programa escreveu: os mesmos números estão nos detalhes.
        var resumo = servico.UltimoResultado?.ResumoEstoque;
        Assert.NotNull(resumo);
        Assert.Contains($"Recebidos: {resumo.Recebidos}", vm.DetalhesSincronizacao);
        Assert.Contains($"Atualizados: {resumo.Atualizados}", vm.DetalhesSincronizacao);
        Assert.Contains($"SKUs desconhecidos: {resumo.SemCadastro}", vm.DetalhesSincronizacao);
        Assert.True(resumo.AmostraSemCadastro.Count <= 10);

        // Repetir não muda o veredito: cadastro no site não aparece sozinho.
        await vm.SincronizarAsync();
        ConferirFaixaDoEstoque(vm, catalogo);
    }

    [WpfFact]
    public async Task Botao_ComOExeReal_CriaAsVendasEElasAparecemEmVendasSite_SemDuplicarNaSegundaVez()
    {
        if (!Configurado(out var exe, out var conexao, out var pedidos))
            return;

        // Sem a variável, o cenário das vendas é o de sempre: o site tem só alguns produtos da loja.
        var catalogo = Catalogo() is { Length: > 0 } declarado ? declarado : "parcial";

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
        // As vendas foram criadas; a cor da faixa depende de o site conhecer os produtos da loja.
        ConferirFaixaDoEstoque(vm, catalogo);
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
        ConferirQueNadaSecretoApareceu(vm);

        // 5. Segunda vez: o botão roda de novo e NADA é duplicado.
        var vendasAntesDaSegunda = vm.Vendas.Select(v => v.NumeroVenda).ToList();
        await vm.SincronizarAsync();

        ConferirFaixaDoEstoque(vm, catalogo);
        Assert.Equal(totalAntes + pedidos.Length, vm.TotalItens);
        Assert.Equal(vendasAntesDaSegunda, vm.Vendas.Select(v => v.NumeroVenda).ToList());
        Assert.Contains("nenhuma venda pendente", vm.DetalhesSincronizacao);
    }
}
