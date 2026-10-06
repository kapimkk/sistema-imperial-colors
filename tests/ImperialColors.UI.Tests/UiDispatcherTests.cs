using ImperialColors.UI.Helpers;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// O <c>Application</c> do WPF é único no processo e fica preso à thread que o criou. Em teste, essa
/// é a thread do PRIMEIRO teste — que termina — e os testes seguintes rodam em outras. Um
/// <c>Invoke</c> para o dispatcher dessa thread morta nunca volta: era o que travava, por nove
/// minutos, qualquer ViewModel que mexesse em <c>Carregando</c> fora do primeiro teste.
/// </summary>
public class UiDispatcherTests
{
    /// <summary>Garante um <c>Application</c> cujo dispatcher pertence a uma thread já encerrada.</summary>
    private static void GarantirApplicationDeThreadEncerrada()
    {
        var criador = new Thread(() =>
        {
            if (System.Windows.Application.Current is null)
                _ = new System.Windows.Application();
        });
        criador.SetApartmentState(ApartmentState.STA);
        criador.Start();
        criador.Join();
    }

    private static bool TerminaEmAte(TimeSpan limite, Action acao) => Task.Run(acao).Wait(limite);

    [Fact]
    public void ExecutarNaUi_ComDispatcherDeThreadEncerrada_ExecutaEmVezDeTravar()
    {
        GarantirApplicationDeThreadEncerrada();
        var executou = false;

        var terminou = TerminaEmAte(TimeSpan.FromSeconds(15), () => UiDispatcher.ExecutarNaUi(() => executou = true));

        Assert.True(terminou, "ExecutarNaUi ficou esperando um dispatcher que não existe mais");
        Assert.True(executou);
    }

    [Fact]
    public void PostarNaUi_ComDispatcherDeThreadEncerrada_ExecutaEmVezDeSumir()
    {
        GarantirApplicationDeThreadEncerrada();
        var executou = false;

        var terminou = TerminaEmAte(TimeSpan.FromSeconds(15), () => UiDispatcher.PostarNaUi(() => executou = true));

        Assert.True(terminou);
        Assert.True(executou, "a ação foi agendada num dispatcher morto e nunca rodaria");
    }

    [Fact]
    public async Task ExecutarNaUiAsync_ComDispatcherDeThreadEncerrada_ExecutaEmVezDeTravar()
    {
        GarantirApplicationDeThreadEncerrada();
        var executou = false;

        var tarefa = Task.Run(() => UiDispatcher.ExecutarNaUiAsync(() =>
        {
            executou = true;
            return Task.CompletedTask;
        }));
        var terminou = await Task.WhenAny(tarefa, Task.Delay(TimeSpan.FromSeconds(15))) == tarefa;

        Assert.True(terminou, "ExecutarNaUiAsync ficou esperando um dispatcher que não existe mais");
        Assert.True(executou);
    }

    [Fact]
    public void ExecutarNaUi_PropagaExcecaoDaAcao()
    {
        GarantirApplicationDeThreadEncerrada();

        Assert.Throws<InvalidOperationException>(() => UiDispatcher.ExecutarNaUi(() => throw new InvalidOperationException("da ação")));
    }
}
