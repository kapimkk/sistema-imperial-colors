namespace ImperialColors.Application.DTOs;

public enum StatusSincronizacaoSite
{
    /// <summary>O ImperialSync terminou com código 0.</summary>
    Concluida = 1,

    /// <summary>Terminou, mas deixou algo para o operador conferir (vendas que a loja recusou,
    /// por exemplo): o código de saída 10 do ImperialSync.</summary>
    ConcluidaComAtencao = 2,

    /// <summary>O ImperialSync rodou e terminou com um código de erro.</summary>
    Falhou = 3,

    /// <summary>O ImperialSync.exe não está na pasta do sistema: nada foi executado.</summary>
    ExecutavelNaoEncontrado = 4,

    /// <summary>Já existe uma sincronização em andamento (neste sistema, neste computador ou em
    /// outro computador da loja): esta chamada não rodou nada.</summary>
    JaEmExecucao = 5,

    /// <summary>Passou do tempo máximo e o processo foi encerrado à força.</summary>
    TempoEsgotado = 6,

    /// <summary>A sincronização foi interrompida antes de terminar.</summary>
    Cancelada = 7,

    /// <summary>O Windows não conseguiu iniciar o ImperialSync.exe.</summary>
    NaoIniciou = 8
}

/// <summary>
/// O que a tela precisa saber de uma execução do ImperialSync: como terminou, uma mensagem para
/// o operador e as últimas linhas do que o programa escreveu — já filtradas, sem segredo, CPF
/// nem dado de conexão.
/// </summary>
public sealed class ResultadoSincronizacaoSite
{
    public StatusSincronizacaoSite Status { get; init; }

    /// <summary>Código de saída do ImperialSync; nulo quando ele não chegou a terminar sozinho.</summary>
    public int? CodigoSaida { get; init; }

    public string Mensagem { get; init; } = string.Empty;
    public IReadOnlyList<string> Detalhes { get; init; } = Array.Empty<string>();
    public TimeSpan Duracao { get; init; }

    /// <summary>Verdadeiro quando o processo chegou a ser iniciado (terminando bem, mal ou à
    /// força): é quando faz sentido recarregar a lista, porque vendas podem ter sido criadas.</summary>
    public bool ProcessoExecutado { get; init; }

    public bool Sucesso => Status == StatusSincronizacaoSite.Concluida;
}
