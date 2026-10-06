using ImperialColors.Application.Helpers;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// O que o ImperialSync escreve no console passa por este filtro antes de aparecer na tela
/// "Vendas Site" ou no log: segredo, senha, hash/assinatura, CPF/CNPJ, e-mail e dados de
/// conexão nunca podem aparecer, mas o que o operador precisa ler (pedido, número da venda,
/// contagens, tempos) tem que passar intacto.
/// </summary>
public class SaidaProcessoSeguraHelperTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\t")]
    public void LinhaEmBranco_NaoViraLinha(string? linha)
        => Assert.Null(SaidaProcessoSeguraHelper.Sanitizar(linha));

    [Theory]
    [InlineData("Imperial Colors - Sincronização")]
    [InlineData("Banco local: conectado (integração de vendas na versão 1)")]
    [InlineData("Vendas: 2 recebida(s) da API.")]
    [InlineData("  Venda IC-2026-000001: criada na loja como 20261006-0001 (0,1 s).")]
    [InlineData("Vendas: 2 criada(s) na loja, 0 já existia(m), 0 recusada(s).")]
    [InlineData("Recebidos: 222")]
    [InlineData("Duração: 0,1 s")]
    [InlineData("Operação 01a112bd-d8e3-7ac0-acb1-9432eabb21fe aplicada.")]
    public void TextoDoOperador_PassaIntacto(string linha)
        => Assert.Equal(linha.TrimEnd(), SaidaProcessoSeguraHelper.Sanitizar(linha));

    [Theory]
    [InlineData("STORE_DB_PASSWORD=hunter2", "STORE_DB_PASSWORD=[oculto]")]
    [InlineData("SYNC_SALES_SECRET_HEX=aaaaaaaa", "SYNC_SALES_SECRET_HEX=[oculto]")]
    [InlineData("password: s3nh4", "password=[oculto]")]
    [InlineData("senha = \"com espaço\"", "senha=[oculto]")]
    [InlineData("token=abc.def.ghi", "token=[oculto]")]
    [InlineData("Authorization: Bearer abc.def.ghi", "Authorization=[oculto]")]
    public void AtribuicaoDeSegredo_TemOValorEscondido(string linha, string esperado)
    {
        var resultado = SaidaProcessoSeguraHelper.Sanitizar(linha);

        Assert.NotNull(resultado);
        Assert.DoesNotContain("hunter2", resultado);
        Assert.DoesNotContain("s3nh4", resultado);
        Assert.DoesNotContain("com espaço", resultado);
        Assert.DoesNotContain("abc.def.ghi", resultado);
        Assert.Equal(esperado, resultado);
    }

    [Fact]
    public void CabecalhoBearerSozinho_TemOTokenEscondido()
        => Assert.Equal("usando Bearer [oculto]", SaidaProcessoSeguraHelper.Sanitizar("usando Bearer abc123.def"));

    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=imperial_colors;Username=postgres;Password=abc")]
    [InlineData("falha em Server=10.0.0.5;Port=5432;Database=x;User Id=u;Password=p")]
    [InlineData("postgres://usuario:senha@localhost:5432/imperial_colors")]
    [InlineData("postgresql://u:p@h/db")]
    public void StringDeConexao_NaoApareceNemEmParte(string linha)
    {
        var resultado = SaidaProcessoSeguraHelper.Sanitizar(linha);

        Assert.NotNull(resultado);
        Assert.Contains("[conexão oculta]", resultado);
        Assert.DoesNotContain("5432", resultado);
        Assert.DoesNotContain("usuario", resultado);
        Assert.DoesNotContain("senha", resultado);
        Assert.DoesNotContain("localhost", resultado);
    }

    [Theory]
    [InlineData("assinatura b7c227706bcf5a1e9d3c4b8a7f6e5d4c3b2a19087f6e5d4c3b2a1908f7e6d5c4", "assinatura [oculto]")]
    [InlineData("hash d4ca47a9fa44d4ca47a9fa44d4ca47a9fa44", "hash [oculto]")]
    public void HashEAssinatura_Longos_SaoEscondidos(string linha, string esperado)
        => Assert.Equal(esperado, SaidaProcessoSeguraHelper.Sanitizar(linha));

    [Theory]
    [InlineData("comprador 529.982.247-25 aprovado", "comprador [CPF oculto] aprovado")]
    [InlineData("comprador 52998224725 aprovado", "comprador [CPF oculto] aprovado")]
    [InlineData("empresa 61.736.241/0001-98", "empresa [CNPJ oculto]")]
    [InlineData("empresa 61736241000198", "empresa [CNPJ oculto]")]
    [InlineData("contato maria.silva+loja@example.com.br", "contato [e-mail oculto]")]
    public void DocumentosEEmails_SaoEscondidos(string linha, string esperado)
        => Assert.Equal(esperado, SaidaProcessoSeguraHelper.Sanitizar(linha));

    [Fact]
    public void NumeroDeVendaEDePedido_NaoSaoConfundidosComDocumento()
    {
        const string linha = "Venda IC-2026-000123 virou 20261006-0042 em 06/10/2026 às 16:45";

        Assert.Equal(linha, SaidaProcessoSeguraHelper.Sanitizar(linha));
    }

    [Fact]
    public void SequenciasAnsiECaracteresDeControle_SaoRemovidos()
    {
        var resultado = SaidaProcessoSeguraHelper.Sanitizar("\u001b[32mSincronização concluída.\u001b[0m\u0007\u0000");

        Assert.Equal("Sincronização concluída.", resultado);
    }

    [Fact]
    public void LinhaLonga_EhCortadaComReticencias()
    {
        var resultado = SaidaProcessoSeguraHelper.Sanitizar(new string('x', 1000), maximoCaracteres: 50);

        Assert.NotNull(resultado);
        Assert.Equal(51, resultado.Length);
        Assert.EndsWith("…", resultado);
    }

    [Fact]
    public void SequenciaLongaDeHexadecimais_EhTratadaComoSegredo_MesmoSemRotulo()
        => Assert.Equal("[oculto]", SaidaProcessoSeguraHelper.Sanitizar(new string('a', 1000)));

    [Fact]
    public void LinhaLongaPadrao_RespeitaOLimiteDoHelper()
    {
        var resultado = SaidaProcessoSeguraHelper.Sanitizar(new string('x', 5000));

        Assert.NotNull(resultado);
        Assert.Equal(SaidaProcessoSeguraHelper.MaximoCaracteresPorLinha + 1, resultado.Length);
    }

    [Fact]
    public void VariosSegredosNaMesmaLinha_SaoTodosEscondidos()
    {
        const string linha = "STORE_DB_PASSWORD=abc SYNC_AGENT_SECRET=def 52998224725 a@b.com";

        var resultado = SaidaProcessoSeguraHelper.Sanitizar(linha);

        Assert.NotNull(resultado);
        Assert.DoesNotContain("abc", resultado);
        Assert.DoesNotContain("def", resultado);
        Assert.DoesNotContain("52998224725", resultado);
        Assert.DoesNotContain("a@b.com", resultado);
    }
}
