using ImperialColors.Application.DTOs;

namespace ImperialColors.Application.Interfaces;

/// <summary>
/// Executa o ImperialSync.exe — e só isso. Quem fala com o site (HTTPS, assinatura, fila de
/// vendas, reservas e confirmações) e quem cria a venda no banco é o ImperialSync; este sistema
/// apenas o dispara, espera, interpreta o código de saída e mostra o resultado.
/// </summary>
public interface ISincronizacaoSiteService
{
    /// <summary>Verdadeiro enquanto o ImperialSync está rodando por este sistema.</summary>
    bool EmExecucao { get; }

    /// <summary>Disparado quando <see cref="EmExecucao"/> muda (começou ou terminou). Pode vir
    /// de qualquer thread: quem atualiza tela precisa voltar para a thread da interface.</summary>
    event EventHandler? EmExecucaoAlterada;

    /// <summary>Resultado da execução mais recente (nulo antes da primeira).</summary>
    ResultadoSincronizacaoSite? UltimoResultado { get; }

    /// <summary>Caminho onde o sistema espera encontrar o ImperialSync.exe.</summary>
    string CaminhoExecutavel { get; }

    /// <summary>
    /// Roda <c>ImperialSync.exe --once</c> (vendas do site para a loja e estoque da loja para o
    /// site) sem travar quem chamou. Nunca lança por causa do programa: tudo vira um
    /// <see cref="ResultadoSincronizacaoSite"/>. Uma segunda chamada enquanto uma execução está em
    /// andamento não roda nada e devolve <see cref="StatusSincronizacaoSite.JaEmExecucao"/>.
    /// </summary>
    Task<ResultadoSincronizacaoSite> SincronizarAsync(CancellationToken cancellationToken = default);
}
