using ImperialColors.Infrastructure.Atualizacao;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// A atualização do sistema troca os arquivos da pasta do programa por cima. O
/// <c>ImperialSync.env</c> (senha do banco e segredos do site) fica nessa mesma pasta e nenhum
/// pacote pode sobrescrevê-lo; o <c>ImperialSync.exe</c>, que o pacote não traz, não pode ser apagado.
/// </summary>
public class AtualizacaoPreservaImperialSyncTests
{
    [Fact]
    public void ArquivoDeConfiguracaoDoImperialSync_EhPreservadoPelaAtualizacao()
    {
        Assert.Contains("ImperialSync.env", ScriptAplicacaoAtualizacao.ArquivosPreservados);
        Assert.Contains(".env", ScriptAplicacaoAtualizacao.ArquivosPreservados);
    }

    [Fact]
    public void ScriptDeTroca_PulaOsPreservados_ENuncaApagaArquivoQueNaoEstaNoPacote()
    {
        var script = ScriptAplicacaoAtualizacao.Gerar();

        Assert.Contains("'.env', 'ImperialSync.env'", script);
        Assert.Contains("if ($preservados -contains $_.Name) { return }", script);
        // A troca só copia (Copy-Item) o que está no pacote: ImperialSync.exe e .env ficam onde estão.
        Assert.DoesNotContain("Remove-Item -LiteralPath $Destino", script);
    }
}
