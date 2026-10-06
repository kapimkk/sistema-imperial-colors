using ImperialColors.Domain.ReadModels;
using ImperialColors.Infrastructure.Repositories;
using Npgsql;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Partes do repositório de "Vendas Site" que não precisam de banco: como uma falha do
/// PostgreSQL vira "integração não instalada" ou "sem permissão", e como o texto digitado vira um
/// padrão de busca sem curingas escondidos.
/// </summary>
public class VendaSiteRepositoryTests
{
    private static PostgresException Falha(string sqlState)
        => new("mensagem de teste", "ERROR", "ERROR", sqlState);

    [Theory]
    [InlineData(PostgresErrorCodes.UndefinedTable, SituacaoIntegracaoSite.NaoInstalada)]
    [InlineData(PostgresErrorCodes.InvalidSchemaName, SituacaoIntegracaoSite.NaoInstalada)]
    [InlineData(PostgresErrorCodes.InsufficientPrivilege, SituacaoIntegracaoSite.SemPermissao)]
    public void FalhaDeAcesso_ViraSituacaoDaIntegracao(string sqlState, SituacaoIntegracaoSite esperada)
        => Assert.Equal(esperada, VendaSiteRepository.ClassificarFalhaDeAcesso(Falha(sqlState)));

    [Fact]
    public void FalhaEmbrulhada_EhClassificadaPelaCausa()
    {
        var embrulhada = new InvalidOperationException("erro ao executar", Falha(PostgresErrorCodes.InsufficientPrivilege));

        Assert.Equal(SituacaoIntegracaoSite.SemPermissao, VendaSiteRepository.ClassificarFalhaDeAcesso(embrulhada));
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    [InlineData(PostgresErrorCodes.SyntaxError)]
    [InlineData(PostgresErrorCodes.QueryCanceled)]
    public void OutrosErrosDoBanco_NaoSaoEscondidos(string sqlState)
        => Assert.Null(VendaSiteRepository.ClassificarFalhaDeAcesso(Falha(sqlState)));

    [Fact]
    public void ErroQueNaoEhDoBanco_NaoEhEscondido()
    {
        Assert.Null(VendaSiteRepository.ClassificarFalhaDeAcesso(new TimeoutException()));
        Assert.Null(VendaSiteRepository.ClassificarFalhaDeAcesso(new InvalidOperationException("x", new IOException())));
    }

    [Theory]
    [InlineData("IC-2026-000001", "%IC-2026-000001%")]
    [InlineData("20261006_0001", "%20261006\\_0001%")]
    [InlineData("100%", "%100\\%%")]
    [InlineData("a\\b", "%a\\\\b%")]
    [InlineData("maria da silva", "%maria da silva%")]
    public void TextoDigitado_ViraContemSemCuringa(string digitado, string esperado)
        => Assert.Equal(esperado, VendaSiteRepository.MontarPadraoContem(digitado));
}
