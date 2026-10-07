namespace ImperialColors.Application.DTOs;

public enum StatusSincronizacaoSite
{
    /// <summary>O ImperialSync terminou com código 0 e nenhum produto enviado ficou sem
    /// cadastro no site: a sincronização foi efetiva.</summary>
    Concluida = 1,

    /// <summary>Terminou, mas deixou algo para o operador conferir: vendas que a loja recusou
    /// (código 10) ou produtos da loja sem cadastro no catálogo do site (códigos 14 e 15, ou o
    /// próprio resumo do estoque). A comunicação funcionou — é alerta, não falha.</summary>
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
/// O que o site fez com o estoque enviado, lido do resumo que o ImperialSync escreve ao final da
/// rodada. "O site recebeu" não é "o site sincronizou": produto que não existe no catálogo do
/// site é recebido e fica sem estoque lá.
/// </summary>
public sealed class ResumoEstoqueSite
{
    /// <summary>Produtos que o site recebeu (linha <c>Recebidos:</c>).</summary>
    public int Recebidos { get; init; }

    /// <summary>Produtos do catálogo do site que tiveram o estoque gravado (linha <c>Atualizados:</c>).</summary>
    public int Atualizados { get; init; }

    /// <summary>Produtos da loja que não existem no catálogo do site (linha <c>SKUs desconhecidos:</c>).</summary>
    public int SemCadastro { get; init; }

    /// <summary>Alguns dos códigos sem cadastro: amostra limitada, só com códigos válidos.</summary>
    public IReadOnlyList<string> AmostraSemCadastro { get; init; } = Array.Empty<string>();

    public bool TemSemCadastro => SemCadastro > 0;

    /// <summary>Nenhum produto enviado existe no site: nada foi efetivamente sincronizado.</summary>
    public bool NenhumReconhecido => SemCadastro > 0 && SemCadastro >= Recebidos;
}

/// <summary>
/// O que a tela precisa saber de uma execução do ImperialSync: como terminou, uma mensagem para
/// o operador e as últimas linhas do que o programa escreveu — já filtradas, sem segredo, CPF
/// nem dado de conexão.
/// </summary>
public sealed class ResultadoSincronizacaoSite
{
    public StatusSincronizacaoSite Status { get; init; }

    /// <summary>Os números do estoque desta rodada; nulo quando o programa não chegou a
    /// escrever o resumo (falhou antes, rodou só vendas...).</summary>
    public ResumoEstoqueSite? ResumoEstoque { get; init; }

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
