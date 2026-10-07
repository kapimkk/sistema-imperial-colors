using System.Globalization;
using ImperialColors.Application.DTOs;

namespace ImperialColors.Application.Helpers;

/// <summary>
/// Textos mostrados ao operador e tradução dos códigos de saída do ImperialSync
/// (<c>ImperialSync.exe --help</c>) em algo que ele entenda e saiba o que fazer.
///
/// Os números pertencem ao contrato do ImperialSync, não ao sistema: se o programa ganhar um
/// código novo, ele cai em <see cref="InterpretarCodigoSaida"/> como "terminou com o código N" —
/// nunca como sucesso.
///
/// <b>Sucesso (faixa verde) só quando o estoque foi de fato sincronizado:</b> código 0 E nenhum
/// produto enviado sem cadastro no site. "A API aceitou" com todos os SKUs desconhecidos é
/// <see cref="StatusSincronizacaoSite.ConcluidaComAtencao"/> (alerta), nunca sucesso — e nunca
/// erro técnico, porque rede, assinatura e banco funcionaram.
/// </summary>
public static class SincronizacaoSiteMensagens
{
    /// <summary>Estoque enviado, mas nenhum produto da loja existe no catálogo do site.</summary>
    public const int CodigoNenhumSkuReconhecido = 14;

    /// <summary>Estoque enviado, mas parte dos produtos da loja não tem cadastro no site.</summary>
    public const int CodigoSkusSemCadastro = 15;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public const string Sincronizando = "Sincronizando com o site...";
    public const string Concluida = "Sincronização concluída com sucesso.";
    public const string ConcluidaComAlerta = "Sincronização concluída com alerta.";
    public const string OrientacaoSemCadastro =
        "Para sincronizar esses produtos, cadastre-os no site com o SKU igual ao Código do Produto da loja.";
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
        // Sem os números (o resumo não veio): o alerta vale do mesmo jeito, com o texto genérico.
        CodigoNenhumSkuReconhecido => (StatusSincronizacaoSite.ConcluidaComAtencao,
            $"{ConcluidaComAlerta} O estoque foi enviado, mas nenhum SKU foi encontrado no catálogo do site."),
        CodigoSkusSemCadastro => (StatusSincronizacaoSite.ConcluidaComAtencao,
            $"{ConcluidaComAlerta} Há produtos da loja sem cadastro no catálogo do site."),
        _ => (StatusSincronizacaoSite.Falhou, $"O ImperialSync terminou com o código {codigo}. Veja os detalhes abaixo.")
    };

    /// <summary>
    /// O resultado da rodada: o código de saída E o que o resumo do estoque diz. Os dois precisam
    /// concordar para ser sucesso; basta um apontar produto sem cadastro para ser alerta.
    /// <list type="bullet">
    /// <item>código 0, 14 ou 15 com produto sem cadastro no resumo → alerta, com os números;</item>
    /// <item>código 14 ou 15 sem resumo → alerta, com o texto genérico do código;</item>
    /// <item>código 0 sem produto sem cadastro → sucesso;</item>
    /// <item>outro alerta (vendas que precisam de atenção) → o alerta do estoque entra junto;</item>
    /// <item>falha (rede, assinatura, banco, configuração...) → continua falha: o resumo, se
    /// existir, não a transforma em alerta.</item>
    /// </list>
    /// </summary>
    public static (StatusSincronizacaoSite Status, string Mensagem) Interpretar(int codigo, ResumoEstoqueSite? resumo)
    {
        var (status, mensagem) = InterpretarCodigoSaida(codigo);
        if (resumo is not { TemSemCadastro: true })
            return (status, mensagem);

        var alerta = DescreverSemCadastro(resumo);
        if (codigo is 0 or CodigoNenhumSkuReconhecido or CodigoSkusSemCadastro)
            return (StatusSincronizacaoSite.ConcluidaComAtencao, $"{ConcluidaComAlerta} {alerta}");

        return status == StatusSincronizacaoSite.ConcluidaComAtencao
            ? (status, $"{mensagem} Além disso, {alerta}")
            : (status, mensagem);
    }

    /// <summary>A rodada terminou apontando produto da loja sem cadastro no site (pelo código de
    /// saída ou pelo resumo): é quando a tela orienta o operador a cadastrá-los.</summary>
    public static bool HaProdutoSemCadastro(int? codigo, ResumoEstoqueSite? resumo)
        => resumo is { TemSemCadastro: true } || codigo is CodigoNenhumSkuReconhecido or CodigoSkusSemCadastro;

    /// <summary>A frase do alerta com os números ("222 produtos foram enviados, mas nenhum SKU...").</summary>
    public static string DescreverSemCadastro(ResumoEstoqueSite resumo)
    {
        if (resumo.NenhumReconhecido)
        {
            return resumo.Recebidos == 1
                ? "1 produto foi enviado, mas o SKU dele não foi encontrado no catálogo do site."
                : $"{Numero(resumo.Recebidos)} produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site.";
        }

        return resumo.SemCadastro == 1
            ? $"1 dos {Numero(resumo.Recebidos)} produtos enviados não tem cadastro no catálogo do site."
            : $"{Numero(resumo.SemCadastro)} dos {Numero(resumo.Recebidos)} produtos enviados não têm cadastro no catálogo do site.";
    }

    /// <summary>Os três números da rodada numa linha: recebidos, atualizados e sem cadastro.</summary>
    public static string DescreverResumo(ResumoEstoqueSite resumo)
        => $"Recebidos pelo site: {Numero(resumo.Recebidos)}  ·  Atualizados: {Numero(resumo.Atualizados)}  ·  SKUs sem cadastro: {Numero(resumo.SemCadastro)}";

    /// <summary>A amostra dos códigos sem cadastro, dizendo quantos de quantos; vazio se não houver.</summary>
    public static string DescreverAmostra(ResumoEstoqueSite resumo)
    {
        if (!resumo.TemSemCadastro || resumo.AmostraSemCadastro.Count == 0)
            return string.Empty;

        var mostrados = resumo.AmostraSemCadastro.Count;
        var quantos = mostrados < resumo.SemCadastro
            ? $"{Numero(mostrados)} de {Numero(resumo.SemCadastro)}"
            : Numero(mostrados);
        return $"Exemplos de SKUs sem cadastro ({quantos}): {string.Join(", ", resumo.AmostraSemCadastro)}";
    }

    private static string Numero(int valor) => valor.ToString("N0", PtBr);
}
