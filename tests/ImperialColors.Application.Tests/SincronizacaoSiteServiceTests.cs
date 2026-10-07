using System.Diagnostics;
using System.Text;
using ImperialColors.Application.DTOs;
using ImperialColors.Infrastructure.Configuration;
using ImperialColors.Infrastructure.Services;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// O serviço que o botão "Sincronizar com o Site" usa para executar o ImperialSync.exe.
///
/// Estes testes executam PROCESSOS DE VERDADE: um script .cmd temporário faz o papel do
/// ImperialSync.exe (escreve linhas, termina com o código que o teste pedir, demora, etc.). O
/// ImperialSync.exe real é exercitado à parte (<c>SincronizacaoSiteExeRealTests</c>, na suíte de
/// UI, e na validação ponta a ponta com a API do site).
/// </summary>
public class SincronizacaoSiteServiceTests
{
    // ---------------------------------------------------------------------------------------
    // Infra de teste
    // ---------------------------------------------------------------------------------------

    private sealed class ProgramaDeTeste : IDisposable
    {
        // 12 hexadecimais: o nome da pasta aparece na saída e, com 32, o filtro o trataria como segredo.
        public string Pasta { get; } = Path.Combine(Path.GetTempPath(), "imperialsync-teste-" + Guid.NewGuid().ToString("N")[..12]);
        public string Caminho { get; }

        public ProgramaDeTeste(string script, string nomeArquivo = "ImperialSync.cmd")
        {
            Directory.CreateDirectory(Pasta);
            Caminho = Path.Combine(Pasta, nomeArquivo);
            File.WriteAllText(Caminho, "@echo off\r\n" + script.Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n", new UTF8Encoding(false));
        }

        public void Dispose()
        {
            try { Directory.Delete(Pasta, recursive: true); }
            catch (IOException) { /* o Windows ainda pode estar soltando o arquivo */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static SincronizacaoSiteService Criar(
        ProgramaDeTeste programa, TimeSpan? tempoMaximo = null, int maximoLinhas = 120)
        => new(new SincronizacaoSiteOptions
        {
            CaminhoExecutavel = programa.Caminho,
            TempoMaximo = tempoMaximo ?? TimeSpan.FromSeconds(60),
            MaximoLinhasSaida = maximoLinhas
        });

    private static async Task EsperarAsync(Func<bool> condicao, TimeSpan limite)
    {
        var relogio = Stopwatch.StartNew();
        while (!condicao())
        {
            Assert.True(relogio.Elapsed < limite, "a condição esperada não aconteceu a tempo");
            await Task.Delay(25);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Execução normal
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task CodigoZero_EhSucesso_ComAsLinhasQueOProgramaEscreveu()
    {
        using var programa = new ProgramaDeTeste("""
            echo Banco local: conectado
            echo Vendas: nenhuma venda pendente.
            exit /b 0
            """);
        var servico = Criar(programa);

        var resultado = await servico.SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.Concluida, resultado.Status);
        Assert.True(resultado.Sucesso);
        Assert.Equal(0, resultado.CodigoSaida);
        Assert.Equal("Sincronização concluída com sucesso.", resultado.Mensagem);
        Assert.True(resultado.ProcessoExecutado);
        Assert.Equal(["Banco local: conectado", "Vendas: nenhuma venda pendente."], resultado.Detalhes);
        Assert.True(resultado.Duracao > TimeSpan.Zero);
        Assert.False(servico.EmExecucao);
        Assert.Same(resultado, servico.UltimoResultado);
    }

    [Fact]
    public async Task OUnicoArgumento_EhOnce()
    {
        using var programa = new ProgramaDeTeste("echo ARGS=[%*]\nexit /b 0");

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Contains("ARGS=[--once]", resultado.Detalhes);
    }

    [Fact]
    public async Task PastaDeTrabalho_EhADoExecutavel()
    {
        using var programa = new ProgramaDeTeste("echo CWD=%CD%\nexit /b 0");

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Contains(resultado.Detalhes, linha => string.Equals(linha, $"CWD={programa.Pasta}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaidaDeErro_TambemEhCapturada()
    {
        using var programa = new ProgramaDeTeste("echo mensagem-no-erro-padrao 1>&2\nexit /b 1");

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Contains("mensagem-no-erro-padrao", resultado.Detalhes);
    }

    [Fact]
    public async Task TextoEmUtf8_ComAcentos_ChegaIntacto()
    {
        // O ImperialSync escreve em UTF-8 ("Sincronização concluída."); lido como outra página
        // de código viraria "SincronizaÃ§Ã£o".
        using var programa = new ProgramaDeTeste("chcp 65001 >nul\necho Sincronização concluída.\nexit /b 0");

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Contains("Sincronização concluída.", resultado.Detalhes);
    }

    [Theory]
    [InlineData(1, StatusSincronizacaoSite.Falhou)]
    [InlineData(2, StatusSincronizacaoSite.Falhou)]
    [InlineData(3, StatusSincronizacaoSite.JaEmExecucao)]
    [InlineData(4, StatusSincronizacaoSite.Falhou)]
    [InlineData(5, StatusSincronizacaoSite.Falhou)]
    [InlineData(6, StatusSincronizacaoSite.Falhou)]
    [InlineData(7, StatusSincronizacaoSite.Falhou)]
    [InlineData(8, StatusSincronizacaoSite.ConcluidaComAtencao)]
    [InlineData(9, StatusSincronizacaoSite.Cancelada)]
    [InlineData(10, StatusSincronizacaoSite.ConcluidaComAtencao)]
    [InlineData(11, StatusSincronizacaoSite.Falhou)]
    [InlineData(12, StatusSincronizacaoSite.Falhou)]
    [InlineData(13, StatusSincronizacaoSite.JaEmExecucao)]
    [InlineData(14, StatusSincronizacaoSite.ConcluidaComAtencao)]
    [InlineData(15, StatusSincronizacaoSite.ConcluidaComAtencao)]
    [InlineData(77, StatusSincronizacaoSite.Falhou)]
    public async Task CodigoDeSaida_ViraOStatusEAMensagemDoContrato(int codigo, StatusSincronizacaoSite esperado)
    {
        using var programa = new ProgramaDeTeste($"exit /b {codigo}");

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Equal(esperado, resultado.Status);
        Assert.Equal(codigo, resultado.CodigoSaida);
        Assert.False(resultado.Sucesso);
        Assert.True(resultado.ProcessoExecutado);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Mensagem));
    }

    [Fact]
    public async Task Codigo2_SemArquivoDeConfiguracao_DizOndeProcurar()
    {
        using var programa = new ProgramaDeTeste("exit /b 2");

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Contains("ImperialSync.env não foi encontrado", resultado.Mensagem);
    }

    [Fact]
    public async Task Codigo2_ComArquivoDeConfiguracao_NaoCulpaOArquivoAusente()
    {
        using var programa = new ProgramaDeTeste("exit /b 2");
        File.WriteAllText(Path.Combine(programa.Pasta, "ImperialSync.env"), "# vazio");

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.DoesNotContain("não foi encontrado", resultado.Mensagem);
        Assert.Contains("configuração do ImperialSync é inválida", resultado.Mensagem);
    }

    // ---------------------------------------------------------------------------------------
    // Estoque: "a API aceitou" não é "sincronizou" — produto sem cadastro no site é ALERTA
    // ---------------------------------------------------------------------------------------

    /// <summary>O que o ImperialSync escreve ao final da rodada de estoque (texto real do programa).</summary>
    private static string ScriptDoResumo(string titulo, int recebidos, int atualizados, int desconhecidos, string? exemplos, int codigo, string? frase = null)
        => string.Join('\n', new[]
        {
            "chcp 65001 >nul",
            "echo Imperial Colors - Sincronização",
            "echo Banco local: conectado",
            $"echo Produtos encontrados: {recebidos}",
            "echo Lotes: 1",
            "echo Sincronizando lote 1/1...",
            $"echo {titulo}",
            frase is null ? null : $"echo {frase}",
            $"echo Recebidos: {recebidos}",
            $"echo Atualizados: {atualizados}",
            $"echo SKUs desconhecidos: {desconhecidos}",
            // "echo" + 3 espaços: o primeiro separa o comando; ficam os dois do recuo real.
            exemplos is null ? null : $"echo   Exemplos (produtos da loja sem cadastro no site): {exemplos}",
            "echo Duração: 0,1 s",
            $"exit /b {codigo}"
        }.Where(linha => linha is not null));

    [Fact]
    public async Task Recebidos222_Atualizados0_Desconhecidos222_EhAlerta_NaoSucesso()
    {
        using var programa = new ProgramaDeTeste(ScriptDoResumo(
            "Sincronização concluída com alerta.", 222, 0, 222, "21201050, 301010001, DIL001", codigo: 14,
            frase: "222 produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site."));

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, resultado.Status);
        Assert.False(resultado.Sucesso);
        Assert.Equal(14, resultado.CodigoSaida);
        Assert.Equal(
            "Sincronização concluída com alerta. 222 produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site.",
            resultado.Mensagem);
        Assert.NotNull(resultado.ResumoEstoque);
        Assert.Equal((222, 0, 222), (resultado.ResumoEstoque.Recebidos, resultado.ResumoEstoque.Atualizados, resultado.ResumoEstoque.SemCadastro));
        Assert.Equal(["21201050", "301010001", "DIL001"], resultado.ResumoEstoque.AmostraSemCadastro);
        Assert.True(resultado.ResumoEstoque.NenhumReconhecido);
        // Terminou e pode ter criado vendas: a tela recarrega a lista normalmente.
        Assert.True(resultado.ProcessoExecutado);
        Assert.Contains("  Exemplos (produtos da loja sem cadastro no site): 21201050, 301010001, DIL001", resultado.Detalhes);
    }

    [Fact]
    public async Task Recebidos222_Atualizados200_Desconhecidos22_EhAlertaParcial()
    {
        using var programa = new ProgramaDeTeste(ScriptDoResumo(
            "Sincronização concluída com alerta.", 222, 200, 22, "P00201, P00202", codigo: 15,
            frase: "22 dos 222 produtos enviados não têm cadastro no catálogo do site."));

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, resultado.Status);
        Assert.False(resultado.Sucesso);
        Assert.Equal(15, resultado.CodigoSaida);
        Assert.Equal(
            "Sincronização concluída com alerta. 22 dos 222 produtos enviados não têm cadastro no catálogo do site.",
            resultado.Mensagem);
        Assert.NotNull(resultado.ResumoEstoque);
        Assert.Equal((222, 200, 22), (resultado.ResumoEstoque.Recebidos, resultado.ResumoEstoque.Atualizados, resultado.ResumoEstoque.SemCadastro));
        Assert.False(resultado.ResumoEstoque.NenhumReconhecido);
    }

    [Fact]
    public async Task Recebidos222_TodosReconhecidos_EhSucesso_ComOsNumeros()
    {
        using var programa = new ProgramaDeTeste(ScriptDoResumo("Sincronização concluída.", 222, 222, 0, exemplos: null, codigo: 0));

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.Concluida, resultado.Status);
        Assert.True(resultado.Sucesso);
        Assert.Equal("Sincronização concluída com sucesso.", resultado.Mensagem);
        Assert.NotNull(resultado.ResumoEstoque);
        Assert.Equal((222, 222, 0), (resultado.ResumoEstoque.Recebidos, resultado.ResumoEstoque.Atualizados, resultado.ResumoEstoque.SemCadastro));
        Assert.Empty(resultado.ResumoEstoque.AmostraSemCadastro);
    }

    [Theory]
    [InlineData(222, 0, 222, "nenhum SKU foi encontrado")]
    [InlineData(222, 200, 22, "22 dos 222 produtos enviados não têm cadastro")]
    public async Task ImperialSyncAntigo_QueTerminaComZeroMesmoSemReconhecer_NaoPassaComoSucesso(
        int recebidos, int atualizados, int desconhecidos, string trecho)
    {
        // A versão 1.1.0 dizia "Sincronização concluída." e saía com 0 neste cenário: o falso positivo.
        using var programa = new ProgramaDeTeste(ScriptDoResumo(
            "Sincronização concluída.", recebidos, atualizados, desconhecidos, "A-1, B-2", codigo: 0));

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, resultado.Status);
        Assert.False(resultado.Sucesso);
        Assert.Equal(0, resultado.CodigoSaida);
        Assert.StartsWith("Sincronização concluída com alerta.", resultado.Mensagem);
        Assert.Contains(trecho, resultado.Mensagem);
    }

    [Theory]
    [InlineData(4, "banco de dados da loja")]   // banco
    [InlineData(5, "recusou")]                   // assinatura (HMAC) / 401 / 403
    [InlineData(6, "indisponível")]              // HTTP fora do ar
    [InlineData(12, "assinatura inválida")]      // resposta sem assinatura válida
    public async Task FalhaDeBancoHttpOuAssinatura_EhErro_MesmoQueOProgramaTenhaEscritoUmResumo(int codigo, string trecho)
    {
        // Rodada em que as vendas falharam depois de o estoque ter sido enviado (com produtos sem
        // cadastro): o que o operador precisa ver é a FALHA, não um "concluída com alerta".
        using var programa = new ProgramaDeTeste(ScriptDoResumo(
            "Sincronização concluída com alerta.", 222, 0, 222, "A-1, B-2", codigo,
            frase: "222 produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site."));

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.Falhou, resultado.Status);
        Assert.False(resultado.Sucesso);
        Assert.Contains(trecho, resultado.Mensagem);
        Assert.DoesNotContain("concluída", resultado.Mensagem);
        // Os números continuam disponíveis para a tela mostrar.
        Assert.NotNull(resultado.ResumoEstoque);
    }

    [Fact]
    public async Task FalhaAntesDoResumo_EhErro_SemNumerosInventados()
    {
        using var programa = new ProgramaDeTeste("""
            chcp 65001 >nul
            echo Sincronizando lote 1/1...
            echo Falha no lote 1/1: a API respondeu HTTP 503. 1>&2
            echo Sincronização NÃO concluída. 1>&2
            exit /b 6
            """);

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.Falhou, resultado.Status);
        Assert.Null(resultado.ResumoEstoque);
        Assert.Contains("Sincronização NÃO concluída.", resultado.Detalhes);
    }

    [Fact]
    public async Task CodigoDeAlertaSemAsLinhasDoResumo_AindaEhAlerta()
    {
        using var programa = new ProgramaDeTeste("exit /b 14");

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, resultado.Status);
        Assert.Null(resultado.ResumoEstoque);
        Assert.Contains("nenhum SKU foi encontrado no catálogo do site", resultado.Mensagem);
    }

    [Fact]
    public async Task VendasComAtencaoEProdutosSemCadastro_OsDoisAlertasNaMensagem()
    {
        using var programa = new ProgramaDeTeste(ScriptDoResumo(
            "Sincronização concluída com alerta.", 222, 200, 22, "P00201", codigo: 10,
            frase: "22 dos 222 produtos enviados não têm cadastro no catálogo do site."));

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, resultado.Status);
        Assert.Contains("vendas do site que precisam de atenção", resultado.Mensagem);
        Assert.Contains("22 dos 222 produtos enviados não têm cadastro", resultado.Mensagem);
    }

    // ---------------------------------------------------------------------------------------
    // Programa ausente ou que não inicia
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ExecutavelInexistente_NaoExecutaNada_ENaoLanca()
    {
        var servico = new SincronizacaoSiteService(new SincronizacaoSiteOptions
        {
            CaminhoExecutavel = Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N"), "ImperialSync.exe")
        });

        var resultado = await servico.SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.ExecutavelNaoEncontrado, resultado.Status);
        Assert.Equal("ImperialSync.exe não foi encontrado na pasta do sistema.", resultado.Mensagem);
        Assert.False(resultado.ProcessoExecutado);
        Assert.Null(resultado.CodigoSaida);
        Assert.False(servico.EmExecucao);
        Assert.Same(resultado, servico.UltimoResultado);
    }

    [Fact]
    public async Task ArquivoQueNaoEhUmPrograma_NaoIniciaMasNaoLanca()
    {
        using var programa = new ProgramaDeTeste("isto nao e um programa", nomeArquivo: "ImperialSync.exe");

        var resultado = await Criar(programa).SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.NaoIniciou, resultado.Status);
        Assert.False(resultado.ProcessoExecutado);
        Assert.Contains("não conseguiu iniciar", resultado.Mensagem);
    }

    // ---------------------------------------------------------------------------------------
    // Uma por vez
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SegundaChamadaEnquantoRoda_NaoExecutaNada_ERespondeNaHora()
    {
        using var programa = new ProgramaDeTeste("ping -n 3 127.0.0.1 >nul\nexit /b 0");
        var servico = Criar(programa);

        var primeira = servico.SincronizarAsync();
        await EsperarAsync(() => servico.EmExecucao, TimeSpan.FromSeconds(10));

        var relogio = Stopwatch.StartNew();
        var segunda = await servico.SincronizarAsync();

        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(1), "a segunda chamada não pode esperar a primeira");
        Assert.Equal(StatusSincronizacaoSite.JaEmExecucao, segunda.Status);
        Assert.False(segunda.ProcessoExecutado);
        Assert.Null(segunda.CodigoSaida);
        Assert.Contains("andamento", segunda.Mensagem);

        var resultadoDaPrimeira = await primeira;

        Assert.Equal(StatusSincronizacaoSite.Concluida, resultadoDaPrimeira.Status);
        Assert.False(servico.EmExecucao);
        // A chamada barrada não apaga o resultado da execução de verdade.
        Assert.Same(resultadoDaPrimeira, servico.UltimoResultado);
    }

    [Fact]
    public async Task DepoisDeTerminar_ATravaEhLiberada()
    {
        using var programa = new ProgramaDeTeste("exit /b 0");
        var servico = Criar(programa);

        var primeira = await servico.SincronizarAsync();
        var segunda = await servico.SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.Concluida, primeira.Status);
        Assert.Equal(StatusSincronizacaoSite.Concluida, segunda.Status);
    }

    [Fact]
    public async Task VariasChamadasAoMesmoTempo_RodamNoMaximoUmaVez()
    {
        using var programa = new ProgramaDeTeste("echo rodou >> \"%~dp0execucoes.txt\"\nping -n 2 127.0.0.1 >nul\nexit /b 0");
        var servico = Criar(programa);

        var chamadas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => servico.SincronizarAsync())));

        Assert.Single(chamadas, c => c.Status == StatusSincronizacaoSite.Concluida);
        Assert.Equal(7, chamadas.Count(c => c.Status == StatusSincronizacaoSite.JaEmExecucao));
        Assert.Single(File.ReadAllLines(Path.Combine(programa.Pasta, "execucoes.txt")));
    }

    // ---------------------------------------------------------------------------------------
    // Prazo e cancelamento
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task PassadoOTempoMaximo_EncerraOProcesso()
    {
        using var programa = new ProgramaDeTeste("echo comecou\nping -n 40 127.0.0.1 >nul\nexit /b 0");
        var servico = Criar(programa, tempoMaximo: TimeSpan.FromSeconds(1));
        var relogio = Stopwatch.StartNew();

        var resultado = await servico.SincronizarAsync();

        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(20), "o processo não foi encerrado no prazo");
        Assert.Equal(StatusSincronizacaoSite.TempoEsgotado, resultado.Status);
        Assert.True(resultado.ProcessoExecutado);
        Assert.Null(resultado.CodigoSaida);
        Assert.Contains("demorou", resultado.Mensagem);
        Assert.Contains("comecou", resultado.Detalhes);
        Assert.False(servico.EmExecucao);
    }

    [Fact]
    public async Task Cancelamento_EncerraOProcessoEDevolveCancelada()
    {
        using var programa = new ProgramaDeTeste("ping -n 40 127.0.0.1 >nul\nexit /b 0");
        var servico = Criar(programa);
        using var cancelamento = new CancellationTokenSource();

        var execucao = servico.SincronizarAsync(cancelamento.Token);
        await EsperarAsync(() => servico.EmExecucao, TimeSpan.FromSeconds(10));
        await cancelamento.CancelAsync();
        var resultado = await execucao;

        Assert.Equal(StatusSincronizacaoSite.Cancelada, resultado.Status);
        Assert.True(resultado.ProcessoExecutado);
        Assert.False(servico.EmExecucao);
    }

    [Fact]
    public async Task CancelamentoJaSolicitado_NaoDeixaProcessoSolto()
    {
        using var programa = new ProgramaDeTeste("ping -n 40 127.0.0.1 >nul\nexit /b 0");
        var servico = Criar(programa);
        using var cancelamento = new CancellationTokenSource();
        await cancelamento.CancelAsync();
        var relogio = Stopwatch.StartNew();

        var resultado = await servico.SincronizarAsync(cancelamento.Token);

        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(20));
        Assert.Equal(StatusSincronizacaoSite.Cancelada, resultado.Status);
        Assert.False(servico.EmExecucao);
    }

    // ---------------------------------------------------------------------------------------
    // Saída segura
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SaidaLonga_GuardaSoAsUltimasLinhas()
    {
        using var programa = new ProgramaDeTeste("for /L %%i in (1,1,500) do echo linha %%i\nexit /b 0");

        var resultado = await Criar(programa, maximoLinhas: 50).SincronizarAsync();

        Assert.Equal(50, resultado.Detalhes.Count);
        Assert.Equal("linha 451", resultado.Detalhes[0]);
        Assert.Equal("linha 500", resultado.Detalhes[^1]);
    }

    [Fact]
    public async Task SegredoCpfEConexao_NuncaChegamAoResultado()
    {
        using var programa = new ProgramaDeTeste("""
            echo STORE_DB_PASSWORD=hunter2
            echo Host=localhost;Port=5432;Database=imperial_colors;Username=postgres;Password=abc123
            echo assinatura b7c227706bcf5a1e9d3c4b8a7f6e5d4c3b2a19087f6e5d4c3b2a1908f7e6d5c4
            echo comprador 52998224725 e maria@example.com
            echo Venda IC-2026-000001: criada na loja como 20261006-0001 (0,1 s).
            exit /b 0
            """);

        var resultado = await Criar(programa).SincronizarAsync();
        var tudo = string.Join('\n', resultado.Detalhes);

        Assert.DoesNotContain("hunter2", tudo);
        Assert.DoesNotContain("abc123", tudo);
        Assert.DoesNotContain("localhost", tudo);
        Assert.DoesNotContain("b7c227706bcf", tudo);
        Assert.DoesNotContain("52998224725", tudo);
        Assert.DoesNotContain("maria@example.com", tudo);
        Assert.Contains("Venda IC-2026-000001: criada na loja como 20261006-0001 (0,1 s).", resultado.Detalhes);
    }

    // ---------------------------------------------------------------------------------------
    // Ambiente do processo
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("DB_PASSWORD", true)]
    [InlineData("DB_HOST", true)]
    [InlineData("db_user", true)]
    [InlineData("FISCAL_API_TOKEN", true)]
    [InlineData("CERTIFICADO_SENHA", true)]
    [InlineData("EMPRESA_SECRET", true)]
    [InlineData("MINHA_API_KEY", true)]
    [InlineData("STORE_DB_PASSWORD", false)]
    [InlineData("STORE_DB_HOST", false)]
    [InlineData("SYNC_AGENT_SECRET", false)]
    [InlineData("SYNC_SALES_SECRET_HEX", false)]
    [InlineData("INVENTORY_DB_PASSWORD", false)]
    [InlineData("PATH", false)]
    [InlineData("SystemRoot", false)]
    [InlineData("TEMP", false)]
    [InlineData("USERPROFILE", false)]
    [InlineData("", false)]
    public void QuemSaiDoAmbiente(string nome, bool sai)
        => Assert.Equal(sai, SincronizacaoSiteService.DeveSairDoAmbiente(nome));

    [Fact]
    public async Task ProcessoFilho_NaoHerdaSegredosDoSistema_MasHerdaAConfiguracaoDoImperialSync()
    {
        // Nomes próprios deste teste: nada a ver com as variáveis reais do sistema.
        var variaveis = new Dictionary<string, string>
        {
            ["DB_IMPERIALTEST_X"] = "sai-por-prefixo",
            ["IMPERIALTEST_SECRET_TOKEN"] = "sai-por-nome-sensivel",
            ["STORE_DB_IMPERIALTEST"] = "fica-por-ser-do-imperialsync",
            ["SYNC_IMPERIALTEST_SECRET"] = "fica-mesmo-com-secret-no-nome",
            ["IMPERIALTEST_NORMAL"] = "fica-por-ser-comum"
        };
        foreach (var (nome, valor) in variaveis)
            Environment.SetEnvironmentVariable(nome, valor);

        try
        {
            // A sonda não escreve "nome=valor": o filtro de saída esconderia o valor de qualquer
            // rótulo com SECRET ou TOKEN no nome, e é justamente isso que não pode confundir o teste.
            using var programa = new ProgramaDeTeste(string.Join('\n', variaveis.Keys.Select(nome =>
                $"if defined {nome} (echo presente {nome}) else (echo ausente {nome})")) + "\nexit /b 0");

            var resultado = await Criar(programa).SincronizarAsync();

            Assert.Contains("ausente DB_IMPERIALTEST_X", resultado.Detalhes);
            Assert.Contains("ausente IMPERIALTEST_SECRET_TOKEN", resultado.Detalhes);
            Assert.Contains("presente STORE_DB_IMPERIALTEST", resultado.Detalhes);
            Assert.Contains("presente SYNC_IMPERIALTEST_SECRET", resultado.Detalhes);
            Assert.Contains("presente IMPERIALTEST_NORMAL", resultado.Detalhes);
        }
        finally
        {
            foreach (var nome in variaveis.Keys)
                Environment.SetEnvironmentVariable(nome, null);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Estado e eventos
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Eventos_AvisamInicioEFim_ComOResultadoJaPublicadoNoFim()
    {
        using var programa = new ProgramaDeTeste("exit /b 0");
        var servico = Criar(programa);
        var avisos = new List<(bool EmExecucao, bool TemResultado)>();
        servico.EmExecucaoAlterada += (_, _) => avisos.Add((servico.EmExecucao, servico.UltimoResultado is not null));

        Assert.Null(servico.UltimoResultado);
        await servico.SincronizarAsync();

        Assert.Equal([(true, false), (false, true)], avisos);
    }

    [Fact]
    public async Task OuvinteQueLanca_NaoDerrubaNemTravaASincronizacao()
    {
        using var programa = new ProgramaDeTeste("exit /b 0");
        var servico = Criar(programa);
        servico.EmExecucaoAlterada += (_, _) => throw new InvalidOperationException("ouvinte com defeito");

        var resultado = await servico.SincronizarAsync();
        var outra = await servico.SincronizarAsync();

        Assert.Equal(StatusSincronizacaoSite.Concluida, resultado.Status);
        Assert.Equal(StatusSincronizacaoSite.Concluida, outra.Status);
        Assert.False(servico.EmExecucao);
    }

    [Fact]
    public void CaminhoPadrao_EhAoLadoDoExecutavelDoSistema()
    {
        var padrao = new SincronizacaoSiteOptions();

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "ImperialSync.exe"), padrao.CaminhoExecutavel);
        Assert.Equal("--once", SincronizacaoSiteOptions.ArgumentoRodadaUnica);
        Assert.Equal("ImperialSync.env", SincronizacaoSiteOptions.NomeArquivoConfiguracao);
        Assert.Equal(padrao.CaminhoExecutavel, new SincronizacaoSiteService(padrao).CaminhoExecutavel);
    }
}
