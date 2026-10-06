using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace ImperialColors.Infrastructure.Services;

/// <summary>
/// Executa <c>ImperialSync.exe --once</c> e traduz o resultado. Nada além disso.
///
/// <b>O que este serviço NÃO faz, de propósito:</b> não chama a API do site, não assina nada, não
/// conhece fila, reserva nem confirmação de venda, não cria venda online e não sincroniza estoque.
/// Tudo isso é do ImperialSync. Aqui existe só o necessário para disparar o programa, esperar,
/// ler o código de saída e mostrar o que aconteceu.
///
/// Garantias:
/// <list type="bullet">
/// <item><b>Uma execução por vez.</b> Uma segunda chamada enquanto há uma em andamento não roda
/// nada e devolve <see cref="StatusSincronizacaoSite.JaEmExecucao"/> (o serviço é singleton, então
/// vale para todas as telas). Entre computadores da loja, quem impede duas execuções é o
/// próprio ImperialSync (códigos 3 e 13).</item>
/// <item><b>Nunca trava a tela:</b> espera de forma assíncrona e a saída é lida por eventos.</item>
/// <item><b>Prazo finito:</b> passado <see cref="SincronizacaoSiteOptions.TempoMaximo"/>, encerra o
/// processo e a árvore dele.</item>
/// <item><b>Saída segura:</b> as linhas guardadas passam por
/// <see cref="SaidaProcessoSeguraHelper"/> (sem segredo, CPF/CNPJ, e-mail ou conexão) e são
/// limitadas em quantidade e tamanho.</item>
/// <item><b>Ambiente enxuto:</b> o processo filho não herda as senhas e os segredos do próprio
/// sistema (DB_PASSWORD, chaves fiscais...). Variáveis <c>STORE_DB_*</c> e <c>SYNC_*</c>, que
/// são do ImperialSync, continuam passando.</item>
/// </list>
/// </summary>
public sealed class SincronizacaoSiteService : ISincronizacaoSiteService
{
    private static readonly string[] FragmentosSensiveis =
        ["PASSWORD", "PASSWD", "SECRET", "TOKEN", "SENHA", "APIKEY", "API_KEY", "PRIVATE_KEY", "CERT"];

    private static readonly string[] PrefixosDoImperialSync = ["STORE_DB_", "SYNC_", "INVENTORY_"];

    private readonly SincronizacaoSiteOptions _options;
    private readonly ILogger<SincronizacaoSiteService>? _logger;
    private readonly SemaphoreSlim _trava = new(1, 1);
    private volatile bool _emExecucao;
    private ResultadoSincronizacaoSite? _ultimoResultado;

    public SincronizacaoSiteService(SincronizacaoSiteOptions? options = null, ILogger<SincronizacaoSiteService>? logger = null)
    {
        _options = options ?? new SincronizacaoSiteOptions();
        _logger = logger;
    }

    public bool EmExecucao => _emExecucao;

    public event EventHandler? EmExecucaoAlterada;

    public ResultadoSincronizacaoSite? UltimoResultado => Volatile.Read(ref _ultimoResultado);

    public string CaminhoExecutavel => _options.CaminhoExecutavel;

    public async Task<ResultadoSincronizacaoSite> SincronizarAsync(CancellationToken cancellationToken = default)
    {
        // Sem esperar: se já há uma execução, o operador precisa saber agora, não depois que ela
        // terminar (nem enfileirar uma segunda por cima).
        if (!_trava.Wait(0))
        {
            return new ResultadoSincronizacaoSite
            {
                Status = StatusSincronizacaoSite.JaEmExecucao,
                Mensagem = SincronizacaoSiteMensagens.JaEmExecucao
            };
        }

        ResultadoSincronizacaoSite resultado;
        try
        {
            _emExecucao = true;
            NotificarEmExecucaoAlterada();

            try
            {
                resultado = await ExecutarAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Falha inesperada ao executar o ImperialSync.");
                resultado = new ResultadoSincronizacaoSite
                {
                    Status = StatusSincronizacaoSite.Falhou,
                    Mensagem = SincronizacaoSiteMensagens.FalhaInesperada
                };
            }

            // O resultado é publicado ANTES de avisar que terminou: quem reage ao evento já o
            // enxerga em UltimoResultado.
            Volatile.Write(ref _ultimoResultado, resultado);
        }
        finally
        {
            _emExecucao = false;
            _trava.Release();
        }

        NotificarEmExecucaoAlterada();
        return resultado;
    }

    private async Task<ResultadoSincronizacaoSite> ExecutarAsync(CancellationToken cancellationToken)
    {
        var caminho = _options.CaminhoExecutavel;
        if (!File.Exists(caminho))
        {
            _logger?.LogWarning("ImperialSync não encontrado em {Caminho}.", caminho);
            return new ResultadoSincronizacaoSite
            {
                Status = StatusSincronizacaoSite.ExecutavelNaoEncontrado,
                Mensagem = SincronizacaoSiteMensagens.ExecutavelNaoEncontrado
            };
        }

        var relogio = Stopwatch.StartNew();
        var saida = new SaidaLimitada(_options.MaximoLinhasSaida);
        using var processo = new Process { StartInfo = MontarProcesso(caminho) };
        processo.OutputDataReceived += (_, e) => saida.Adicionar(e.Data);
        processo.ErrorDataReceived += (_, e) => saida.Adicionar(e.Data);

        try
        {
            if (!processo.Start())
                return NaoIniciou(relogio.Elapsed);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            _logger?.LogError(ex, "Não foi possível iniciar o ImperialSync em {Caminho}.", caminho);
            return NaoIniciou(relogio.Elapsed);
        }

        // O ImperialSync não lê o teclado; fechar a entrada evita que ele herde um handle sem dono.
        processo.StandardInput.Close();
        processo.BeginOutputReadLine();
        processo.BeginErrorReadLine();

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(_options.TempoMaximo);

        try
        {
            await processo.WaitForExitAsync(limite.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            EncerrarProcesso(processo);
            var pelaChamada = cancellationToken.IsCancellationRequested;
            _logger?.LogWarning("ImperialSync encerrado à força ({Motivo}).", pelaChamada ? "cancelado" : "tempo esgotado");
            return new ResultadoSincronizacaoSite
            {
                Status = pelaChamada ? StatusSincronizacaoSite.Cancelada : StatusSincronizacaoSite.TempoEsgotado,
                Mensagem = pelaChamada ? SincronizacaoSiteMensagens.Cancelada : SincronizacaoSiteMensagens.TempoEsgotado,
                Detalhes = saida.Linhas(),
                Duracao = relogio.Elapsed,
                ProcessoExecutado = true
            };
        }

        var codigo = processo.ExitCode;
        var (status, mensagem) = SincronizacaoSiteMensagens.InterpretarCodigoSaida(codigo);
        if (codigo == 2 && !File.Exists(Path.Combine(Path.GetDirectoryName(caminho) ?? string.Empty, SincronizacaoSiteOptions.NomeArquivoConfiguracao)))
            mensagem += $" O arquivo {SincronizacaoSiteOptions.NomeArquivoConfiguracao} não foi encontrado ao lado do ImperialSync.exe.";

        _logger?.LogInformation(
            "ImperialSync terminou com código {Codigo} em {Segundos:N1} s ({Status}).",
            codigo, relogio.Elapsed.TotalSeconds, status);

        return new ResultadoSincronizacaoSite
        {
            Status = status,
            CodigoSaida = codigo,
            Mensagem = mensagem,
            Detalhes = saida.Linhas(),
            Duracao = relogio.Elapsed,
            ProcessoExecutado = true
        };
    }

    private static ResultadoSincronizacaoSite NaoIniciou(TimeSpan duracao) => new()
    {
        Status = StatusSincronizacaoSite.NaoIniciou,
        Mensagem = SincronizacaoSiteMensagens.NaoIniciou,
        Duracao = duracao
    };

    private static ProcessStartInfo MontarProcesso(string caminho)
    {
        var info = new ProcessStartInfo
        {
            FileName = caminho,
            // O ImperialSync procura o ImperialSync.env ao lado do executável, não na pasta atual;
            // mesmo assim a pasta de trabalho é a dele, para o comportamento não depender de onde o
            // sistema foi aberto.
            WorkingDirectory = Path.GetDirectoryName(caminho) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // ArgumentList (e não uma linha de comando montada à mão): sem quebra por espaço ou aspas.
        info.ArgumentList.Add(SincronizacaoSiteOptions.ArgumentoRodadaUnica);

        foreach (var nome in info.Environment.Keys.ToList())
        {
            if (DeveSairDoAmbiente(nome))
                info.Environment.Remove(nome);
        }

        return info;
    }

    /// <summary>
    /// Variáveis do sistema que o ImperialSync não precisa e que carregam segredo (a senha do
    /// banco do sistema vem do .env e fica no ambiente do processo). As do ImperialSync
    /// (<c>STORE_DB_*</c>, <c>SYNC_*</c>, <c>INVENTORY_*</c>) continuam passando: é assim que um
    /// técnico pode configurá-lo sem arquivo.
    /// </summary>
    internal static bool DeveSairDoAmbiente(string? nome)
    {
        if (string.IsNullOrEmpty(nome))
            return false;

        if (PrefixosDoImperialSync.Any(prefixo => nome.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (nome.StartsWith("DB_", StringComparison.OrdinalIgnoreCase))
            return true;

        return FragmentosSensiveis.Any(fragmento => nome.Contains(fragmento, StringComparison.OrdinalIgnoreCase));
    }

    private static void EncerrarProcesso(Process processo)
    {
        try
        {
            if (!processo.HasExited)
                processo.Kill(entireProcessTree: true);

            processo.WaitForExit(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Já terminou entre a checagem e o Kill: é exatamente o que se queria.
        }
    }

    private void NotificarEmExecucaoAlterada()
    {
        try
        {
            EmExecucaoAlterada?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Um ouvinte com defeito não pode derrubar nem travar a sincronização.
            _logger?.LogWarning(ex, "Um ouvinte de EmExecucaoAlterada falhou.");
        }
    }

    /// <summary>As últimas linhas da saída, já filtradas, com limite de quantidade.</summary>
    private sealed class SaidaLimitada
    {
        private readonly int _maximo;
        private readonly Queue<string> _linhas = new();
        private readonly object _trava = new();

        public SaidaLimitada(int maximo) => _maximo = Math.Max(1, maximo);

        public void Adicionar(string? linha)
        {
            var segura = SaidaProcessoSeguraHelper.Sanitizar(linha);
            if (segura is null)
                return;

            lock (_trava)
            {
                _linhas.Enqueue(segura);
                while (_linhas.Count > _maximo)
                    _linhas.Dequeue();
            }
        }

        public IReadOnlyList<string> Linhas()
        {
            lock (_trava)
                return _linhas.ToArray();
        }
    }
}
