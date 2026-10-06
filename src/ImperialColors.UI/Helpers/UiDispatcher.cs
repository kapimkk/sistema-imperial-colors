using System.Windows;
using System.Windows.Threading;

namespace ImperialColors.UI.Helpers;

public static class UiDispatcher
{
    public static void ExecutarNaUi(Action acao)
    {
        var dispatcher = ObterDispatcher();

        if (PodeExecutarNestaThread(dispatcher))
            acao();
        else
            dispatcher.Invoke(acao);
    }

    public static async Task ExecutarNaUiAsync(Func<Task> acao)
    {
        var dispatcher = ObterDispatcher();

        if (PodeExecutarNestaThread(dispatcher))
            await acao();
        else
            await dispatcher.InvokeAsync(acao);
    }

    /// <summary>
    /// Agenda <paramref name="acao"/> na thread da interface SEM esperar ela rodar. É o certo
    /// para quem é avisado por outra thread (um evento de serviço): esperar a interface dentro
    /// do aviso trava a thread do serviço, que pode ser justamente quem a interface espera.
    /// </summary>
    public static void PostarNaUi(Action acao)
    {
        var dispatcher = ObterDispatcher();

        if (PodeExecutarNestaThread(dispatcher))
            acao();
        else
            dispatcher.BeginInvoke(acao);
    }

    private static Dispatcher ObterDispatcher()
        => System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <summary>
    /// Verdadeiro na própria thread do dispatcher — e também quando essa thread já terminou.
    /// Um dispatcher de thread encerrada nunca vai processar um <c>Invoke</c>: esperar por ele
    /// travaria quem chamou para sempre. No programa isso não acontece (a thread da interface vive
    /// até o fim); acontece em teste, em que o <c>Application</c> fica preso à thread do primeiro
    /// teste e os seguintes rodam em outras.
    /// </summary>
    private static bool PodeExecutarNestaThread(Dispatcher dispatcher)
        => dispatcher.CheckAccess() || !dispatcher.Thread.IsAlive;
}
