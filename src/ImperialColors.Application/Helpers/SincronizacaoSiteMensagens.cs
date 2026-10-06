using ImperialColors.Application.DTOs;

namespace ImperialColors.Application.Helpers;

/// <summary>
/// Textos mostrados ao operador e tradução dos códigos de saída do ImperialSync
/// (<c>ImperialSync.exe --help</c>) em algo que ele entenda e saiba o que fazer.
///
/// Os números pertencem ao contrato do ImperialSync, não ao sistema: se o programa ganhar um
/// código novo, ele cai em <see cref="InterpretarCodigoSaida"/> como "terminou com o código N" —
/// nunca como sucesso.
/// </summary>
public static class SincronizacaoSiteMensagens
{
    public const string Sincronizando = "Sincronizando com o site...";
    public const string Concluida = "Sincronização concluída com sucesso.";
    public const string ExecutavelNaoEncontrado = "ImperialSync.exe não foi encontrado na pasta do sistema.";
    public const string JaEmExecucao = "Já existe uma sincronização em andamento. Aguarde ela terminar.";
    public const string NaoIniciou = "O Windows não conseguiu iniciar o ImperialSync.exe. Verifique se o arquivo não está bloqueado ou danificado.";
    public const string TempoEsgotado = "A sincronização demorou mais do que o esperado e foi encerrada. Tente novamente; se repetir, confira a internet e a configuração do ImperialSync.";
    public const string Cancelada = "A sincronização foi interrompida.";
    public const string FalhaInesperada = "Não foi possível executar a sincronização por um erro inesperado do sistema.";

    public static (StatusSincronizacaoSite Status, string Mensagem) InterpretarCodigoSaida(int codigo) => codigo switch
    {
        0 => (StatusSincronizacaoSite.Concluida, Concluida),
        10 => (StatusSincronizacaoSite.ConcluidaComAtencao,
            "Sincronização concluída, mas há vendas do site que precisam de atenção. Confira os pedidos no painel de administração do site."),
        8 => (StatusSincronizacaoSite.ConcluidaComAtencao,
            "O ImperialSync não encontrou produtos para enviar ao site. Confira o cadastro de produtos da loja."),
        3 => (StatusSincronizacaoSite.JaEmExecucao,
            "Já existe outra sincronização em execução neste computador. Aguarde ela terminar e tente de novo."),
        13 => (StatusSincronizacaoSite.JaEmExecucao,
            "Outro computador da loja já está processando as vendas do site neste banco. Aguarde um instante e tente de novo."),
        1 => (StatusSincronizacaoSite.Falhou,
            "O ImperialSync encontrou um erro inesperado. Veja os detalhes abaixo."),
        2 => (StatusSincronizacaoSite.Falhou,
            "A configuração do ImperialSync é inválida. Confira o arquivo ImperialSync.env que fica ao lado do ImperialSync.exe."),
        4 => (StatusSincronizacaoSite.Falhou,
            "O ImperialSync não conseguiu acessar o banco de dados da loja. Confira se o PostgreSQL está ligado e se o usuário e a senha do ImperialSync.env estão certos."),
        5 => (StatusSincronizacaoSite.Falhou,
            "O site recusou o acesso do ImperialSync (credenciais inválidas ou sem permissão). Confira o ImperialSync.env."),
        6 => (StatusSincronizacaoSite.Falhou,
            "O site está indisponível no momento. Verifique a internet e tente novamente."),
        7 => (StatusSincronizacaoSite.Falhou,
            "O site não confirmou o recebimento do estoque. Tente novamente."),
        9 => (StatusSincronizacaoSite.Cancelada, Cancelada),
        11 => (StatusSincronizacaoSite.Falhou,
            "A integração com o site não está instalada neste banco, ou o banco da loja é incompatível. Execute os scripts SQL de instalação do ImperialSync."),
        12 => (StatusSincronizacaoSite.Falhou,
            "A resposta do site não pôde ser validada (assinatura inválida) e foi descartada. Nada foi gravado."),
        _ => (StatusSincronizacaoSite.Falhou, $"O ImperialSync terminou com o código {codigo}. Veja os detalhes abaixo.")
    };
}
