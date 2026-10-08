using ImperialColors.UI.Helpers;
using ImperialColors.UI.Views;
using System.Windows;

namespace ImperialColors.UI.Tests;

internal static class WpfTestBootstrap
{
    private static bool _inicializado;

    public static void Inicializar()
    {
        if (_inicializado)
            return;

        if (System.Windows.Application.Current == null)
            _ = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        FormattingHelper.ConfigurarCulturaThread();
        _inicializado = true;
    }
}
