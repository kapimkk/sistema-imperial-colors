using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Os códigos de saída do ImperialSync (<c>ImperialSync.exe --help</c>) e o que o operador lê
/// para cada um. Só o 0 é "sucesso": um código novo ou desconhecido nunca pode parecer sucesso.
/// </summary>
public class SincronizacaoSiteMensagensTests
{
    [Theory]
    [InlineData(0, StatusSincronizacaoSite.Concluida)]
    [InlineData(10, StatusSincronizacaoSite.ConcluidaComAtencao)]
    [InlineData(8, StatusSincronizacaoSite.ConcluidaComAtencao)]
    [InlineData(3, StatusSincronizacaoSite.JaEmExecucao)]
    [InlineData(13, StatusSincronizacaoSite.JaEmExecucao)]
    [InlineData(9, StatusSincronizacaoSite.Cancelada)]
    [InlineData(1, StatusSincronizacaoSite.Falhou)]
    [InlineData(2, StatusSincronizacaoSite.Falhou)]
    [InlineData(4, StatusSincronizacaoSite.Falhou)]
    [InlineData(5, StatusSincronizacaoSite.Falhou)]
    [InlineData(6, StatusSincronizacaoSite.Falhou)]
    [InlineData(7, StatusSincronizacaoSite.Falhou)]
    [InlineData(11, StatusSincronizacaoSite.Falhou)]
    [InlineData(12, StatusSincronizacaoSite.Falhou)]
    [InlineData(14, StatusSincronizacaoSite.Falhou)]
    [InlineData(99, StatusSincronizacaoSite.Falhou)]
    [InlineData(-1, StatusSincronizacaoSite.Falhou)]
    [InlineData(-1073741819, StatusSincronizacaoSite.Falhou)]
    public void CadaCodigoDeSaida_TemOStatusCerto(int codigo, StatusSincronizacaoSite esperado)
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.InterpretarCodigoSaida(codigo);

        Assert.Equal(esperado, status);
        Assert.False(string.IsNullOrWhiteSpace(mensagem));
    }

    [Fact]
    public void SomenteOZeroEhSucesso()
    {
        var comoSucesso = Enumerable.Range(-5, 200)
            .Where(codigo => SincronizacaoSiteMensagens.InterpretarCodigoSaida(codigo).Status == StatusSincronizacaoSite.Concluida)
            .ToList();

        Assert.Equal([0], comoSucesso);
    }

    [Fact]
    public void Sucesso_UsaOTextoPedidoPeloOperador()
    {
        var (_, mensagem) = SincronizacaoSiteMensagens.InterpretarCodigoSaida(0);

        Assert.Equal("Sincronização concluída com sucesso.", mensagem);
        Assert.Equal("Sincronizando com o site...", SincronizacaoSiteMensagens.Sincronizando);
        Assert.Equal("ImperialSync.exe não foi encontrado na pasta do sistema.", SincronizacaoSiteMensagens.ExecutavelNaoEncontrado);
    }

    [Theory]
    [InlineData(2, "ImperialSync.env")]
    [InlineData(4, "banco de dados da loja")]
    [InlineData(5, "recusou")]
    [InlineData(6, "indisponível")]
    [InlineData(10, "precisam de atenção")]
    [InlineData(11, "scripts SQL")]
    [InlineData(12, "assinatura inválida")]
    [InlineData(13, "Outro computador")]
    public void MensagemDizOQueFazer(int codigo, string trecho)
    {
        var (_, mensagem) = SincronizacaoSiteMensagens.InterpretarCodigoSaida(codigo);

        Assert.Contains(trecho, mensagem);
    }

    [Fact]
    public void CodigoDesconhecido_CitaOCodigoEMandaVerOsDetalhes()
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.InterpretarCodigoSaida(77);

        Assert.Equal(StatusSincronizacaoSite.Falhou, status);
        Assert.Contains("77", mensagem);
        Assert.Contains("detalhes", mensagem);
    }
}
