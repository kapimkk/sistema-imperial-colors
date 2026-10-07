using ImperialColors.Application.Helpers;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// A leitura do resumo do estoque que o ImperialSync escreve ("Recebidos", "Atualizados", "SKUs
/// desconhecidos" e a amostra). Os textos de entrada destes testes são cópias da saída REAL do
/// programa: se o ImperialSync mudar o texto de uma dessas linhas, os testes de lá e estes
/// precisam mudar juntos.
/// </summary>
public class ResumoEstoqueSiteLeitorTests
{
    // Saída real do ImperialSync 1.2.0 quando nenhum produto da loja existe no catálogo do site.
    private static readonly string[] NenhumReconhecido =
    [
        "Imperial Colors - Sincronização",
        "Banco local: conectado (integração de vendas na versão 1)",
        "Vendas: nenhuma venda pendente.",
        "Banco local: conectado",
        "Produtos encontrados: 222",
        "Lotes: 1",
        "Sincronizando lote 1/1...",
        "Sincronização concluída com alerta.",
        "222 produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site.",
        "Recebidos: 222",
        "Atualizados: 0",
        "SKUs desconhecidos: 222",
        "  Exemplos (produtos da loja sem cadastro no site): 21201050, 301010001, 301010002, DIL001",
        "Duração: 0,1 s"
    ];

    // Saída real do ImperialSync 1.1.0 no MESMO cenário: dizia "concluída" e terminava com 0.
    private static readonly string[] NenhumReconhecidoNaVersaoAntiga =
    [
        "Imperial Colors - Sincronização",
        "Banco local: conectado",
        "Produtos encontrados: 222",
        "Lotes: 1",
        "Sincronizando lote 1/1...",
        "Sincronização concluída.",
        "Recebidos: 222",
        "Atualizados: 0",
        "SKUs desconhecidos: 222",
        "  Exemplos (produtos da loja sem cadastro no site): 21201050, 301010001",
        "Duração: 0,1 s"
    ];

    [Fact]
    public void NenhumSkuReconhecido_TrazOsTresNumerosEAAmostra()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(NenhumReconhecido);

        Assert.NotNull(resumo);
        Assert.Equal(222, resumo.Recebidos);
        Assert.Equal(0, resumo.Atualizados);
        Assert.Equal(222, resumo.SemCadastro);
        Assert.Equal(["21201050", "301010001", "301010002", "DIL001"], resumo.AmostraSemCadastro);
        Assert.True(resumo.TemSemCadastro);
        Assert.True(resumo.NenhumReconhecido);
    }

    [Fact]
    public void SaidaDaVersaoAntiga_QueDiziaConcluida_TambemEhLida()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(NenhumReconhecidoNaVersaoAntiga);

        Assert.NotNull(resumo);
        Assert.Equal((222, 0, 222), (resumo.Recebidos, resumo.Atualizados, resumo.SemCadastro));
        Assert.True(resumo.NenhumReconhecido);
    }

    [Fact]
    public void ParteDosSkusSemCadastro_NaoEhNenhumReconhecido()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Sincronização concluída com alerta.",
            "22 dos 222 produtos enviados não têm cadastro no catálogo do site.",
            "Recebidos: 222",
            "Atualizados: 200",
            "SKUs desconhecidos: 22",
            "  Exemplos (produtos da loja sem cadastro no site): P00201, P00202",
            "Duração: 0,4 s"
        ]);

        Assert.NotNull(resumo);
        Assert.Equal((222, 200, 22), (resumo.Recebidos, resumo.Atualizados, resumo.SemCadastro));
        Assert.True(resumo.TemSemCadastro);
        Assert.False(resumo.NenhumReconhecido);
        Assert.Equal(["P00201", "P00202"], resumo.AmostraSemCadastro);
    }

    [Fact]
    public void TodosReconhecidos_SemAmostraESemAlerta()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Sincronização concluída.",
            "Recebidos: 222",
            "Atualizados: 222",
            "SKUs desconhecidos: 0",
            "Duração: 0,3 s"
        ]);

        Assert.NotNull(resumo);
        Assert.Equal((222, 222, 0), (resumo.Recebidos, resumo.Atualizados, resumo.SemCadastro));
        Assert.False(resumo.TemSemCadastro);
        Assert.False(resumo.NenhumReconhecido);
        Assert.Empty(resumo.AmostraSemCadastro);
    }

    [Fact]
    public void ReconhecidosJaEmDiaNoSite_NadaAtualizadoENadaSemCadastro_NaoEhAlerta()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Recebidos: 222",
            "Atualizados: 0",
            "SKUs desconhecidos: 0",
            "Ignorados (já havia leitura mais nova): 222"
        ]);

        Assert.NotNull(resumo);
        Assert.False(resumo.TemSemCadastro);
        Assert.False(resumo.NenhumReconhecido);
    }

    [Fact]
    public void NadaAtualizadoMasParteReconhecida_EhAlertaParcial_NaoNenhumReconhecido()
    {
        // 22 produtos são do catálogo e já estavam em dia; 200 não têm cadastro. "Nenhum reconhecido"
        // depende dos SEM CADASTRO, não de quantos foram gravados.
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Recebidos: 222",
            "Atualizados: 0",
            "SKUs desconhecidos: 200",
            "Ignorados (já havia leitura mais nova): 22"
        ]);

        Assert.NotNull(resumo);
        Assert.True(resumo.TemSemCadastro);
        Assert.False(resumo.NenhumReconhecido);
    }

    [Fact]
    public void LinhasComAHoraDoModoContinuo_TambemSaoLidas()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "[2026-10-06 20:34:42] Recebidos: 1209",
            "[2026-10-06 20:34:42] Atualizados: 1000",
            "[2026-10-06 20:34:42] SKUs desconhecidos: 209",
            "[2026-10-06 20:34:42]   Exemplos (produtos da loja sem cadastro no site): A-1, B-2"
        ]);

        Assert.NotNull(resumo);
        Assert.Equal((1209, 1000, 209), (resumo.Recebidos, resumo.Atualizados, resumo.SemCadastro));
        Assert.Equal(["A-1", "B-2"], resumo.AmostraSemCadastro);
    }

    [Fact]
    public void SemAsLinhasDoResumo_NaoInventaNumeros()
    {
        Assert.Null(ResumoEstoqueSiteLeitor.Ler(null));
        Assert.Null(ResumoEstoqueSiteLeitor.Ler([]));
        Assert.Null(ResumoEstoqueSiteLeitor.Ler(["Banco local: conectado", "Vendas: nenhuma venda pendente."]));
        // Falhou antes de chegar ao resumo.
        Assert.Null(ResumoEstoqueSiteLeitor.Ler(
        [
            "Sincronizando lote 1/1...",
            "Falha no lote 1/1: a API respondeu HTTP 401.",
            "Sincronização NÃO concluída."
        ]));
    }

    [Theory]
    [InlineData("Recebidos: 222", "SKUs desconhecidos: 222")]                    // falta "Atualizados"
    [InlineData("Recebidos: 222", "Atualizados: 0")]                              // falta "SKUs desconhecidos"
    [InlineData("Atualizados: 0", "SKUs desconhecidos: 222")]                    // falta "Recebidos"
    public void ResumoIncompleto_EhComoNaoTerResumo(string primeira, string segunda)
    {
        Assert.Null(ResumoEstoqueSiteLeitor.Ler([primeira, segunda]));
    }

    [Theory]
    [InlineData("Recebidos: abc")]
    [InlineData("Recebidos: -5")]
    [InlineData("Recebidos: 12,5")]
    [InlineData("Recebidos: 1.209")]
    [InlineData("Recebidos: 99999999999")]
    [InlineData("Recebidos: 0")]
    [InlineData("Recebidos:")]
    [InlineData("Total Recebidos: 222")]
    [InlineData("Recebidos: 222 itens")]
    [InlineData("recebidos: 222")]
    public void NumeroForaDoFormato_NaoEhAceito(string recebidos)
    {
        Assert.Null(ResumoEstoqueSiteLeitor.Ler([recebidos, "Atualizados: 0", "SKUs desconhecidos: 1"]));
    }

    [Fact]
    public void LinhaQueSoPareceDoResumo_NaoMudaOsNumeros()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Recebidos: 10",
            "Atualizados: 4",
            "SKUs desconhecidos: 6",
            "Venda IC-2026-000001: criada na loja como 20261006-0001 (Atualizados: 99).",
            "Observação: SKUs desconhecidos: 0"
        ]);

        Assert.NotNull(resumo);
        Assert.Equal((10, 4, 6), (resumo.Recebidos, resumo.Atualizados, resumo.SemCadastro));
    }

    [Fact]
    public void DoisResumosNaSaida_ValeOUltimo_SemMisturarComOAnterior()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Recebidos: 222",
            "Atualizados: 0",
            "SKUs desconhecidos: 222",
            "  Exemplos (produtos da loja sem cadastro no site): ANTIGO-1, ANTIGO-2",
            "Recebidos: 222",
            "Atualizados: 222",
            "SKUs desconhecidos: 0"
        ]);

        Assert.NotNull(resumo);
        Assert.Equal((222, 222, 0), (resumo.Recebidos, resumo.Atualizados, resumo.SemCadastro));
        Assert.Empty(resumo.AmostraSemCadastro);
    }

    [Fact]
    public void SegundoResumoPelaMetade_NaoHerdaNumerosDoPrimeiro()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Recebidos: 222",
            "Atualizados: 222",
            "SKUs desconhecidos: 0",
            "Recebidos: 222"
        ]);

        Assert.Null(resumo);
    }

    [Fact]
    public void Amostra_LimitadaADez_SemRepetir()
    {
        var codigos = Enumerable.Range(1, 25).Select(n => $"P{n:00000}").ToList();
        codigos.Insert(1, "P00001");

        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Recebidos: 30",
            "Atualizados: 0",
            "SKUs desconhecidos: 30",
            "  Exemplos (produtos da loja sem cadastro no site): " + string.Join(", ", codigos)
        ]);

        Assert.NotNull(resumo);
        Assert.Equal(ResumoEstoqueSiteLeitor.MaximoAmostra, resumo.AmostraSemCadastro.Count);
        Assert.Equal(10, resumo.AmostraSemCadastro.Distinct().Count());
        Assert.Equal("P00001", resumo.AmostraSemCadastro[0]);
        Assert.Equal("P00010", resumo.AmostraSemCadastro[^1]);
    }

    [Fact]
    public void Amostra_DescartaOQueNaoEhCodigo_ComoValorMascaradoOuPedacoDeLinha()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Recebidos: 9",
            "Atualizados: 0",
            "SKUs desconhecidos: 9",
            "  Exemplos (produtos da loja sem cadastro no site): DIL001, [CPF oculto], ***, com espaço, A.b_c-d/9, , -hifen, tinta-ação, [oculto], 7893866241914"
        ]);

        Assert.NotNull(resumo);
        Assert.Equal(["DIL001", "A.b_c-d/9", "7893866241914"], resumo.AmostraSemCadastro);
    }

    [Fact]
    public void Amostra_DeLinhaCortada_DescartaOUltimoCodigoQuePodeEstarPelaMetade()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Recebidos: 5",
            "Atualizados: 0",
            "SKUs desconhecidos: 5",
            "  Exemplos (produtos da loja sem cadastro no site): AAA-1, BBB-2, CCC-…"
        ]);

        Assert.NotNull(resumo);
        Assert.Equal(["AAA-1", "BBB-2"], resumo.AmostraSemCadastro);
    }

    [Fact]
    public void Amostra_SemNenhumDesconhecido_EhIgnorada()
    {
        var resumo = ResumoEstoqueSiteLeitor.Ler(
        [
            "Recebidos: 5",
            "Atualizados: 5",
            "SKUs desconhecidos: 0",
            "  Exemplos (produtos da loja sem cadastro no site): SOBRA-1"
        ]);

        Assert.NotNull(resumo);
        Assert.Empty(resumo.AmostraSemCadastro);
    }

    [Fact]
    public void DesconhecidosMaisQueRecebidos_AindaEhAlerta()
    {
        // Incoerente, mas se o site disser isso o operador precisa ver o alerta, não um sucesso.
        var resumo = ResumoEstoqueSiteLeitor.Ler(["Recebidos: 5", "Atualizados: 0", "SKUs desconhecidos: 9"]);

        Assert.NotNull(resumo);
        Assert.True(resumo.TemSemCadastro);
        Assert.True(resumo.NenhumReconhecido);
    }

    [Fact]
    public void DepoisDoFiltroDeSaida_OResumoContinuaLegivel()
    {
        // É o caminho real: o serviço guarda as linhas JÁ filtradas e é delas que o resumo é lido.
        // Um código de 11 dígitos é mascarado pelo filtro (parece CPF): some da amostra, não do total.
        var filtradas = new[]
            {
                "Sincronização concluída com alerta.",
                "Recebidos: 222",
                "Atualizados: 0",
                "SKUs desconhecidos: 222",
                "  Exemplos (produtos da loja sem cadastro no site): 21201050, 12345678901, DIL001, 301010001"
            }
            .Select(linha => SaidaProcessoSeguraHelper.Sanitizar(linha))
            .Where(linha => linha is not null)
            .Select(linha => linha!)
            .ToList();

        var resumo = ResumoEstoqueSiteLeitor.Ler(filtradas);

        Assert.NotNull(resumo);
        Assert.Equal((222, 0, 222), (resumo.Recebidos, resumo.Atualizados, resumo.SemCadastro));
        Assert.Equal(["21201050", "DIL001", "301010001"], resumo.AmostraSemCadastro);
    }

    [Fact]
    public void LinhaDeExemplosMuitoLonga_CortadaPeloFiltro_NaoQuebraALeitura()
    {
        // Letras de H em diante: uma sequência longa só de 0-9/A-F seria tomada por segredo e mascarada.
        var longos = Enumerable.Range(1, 10).Select(n => new string((char)('G' + n), 60) + n).ToList();
        var linha = SaidaProcessoSeguraHelper.Sanitizar(
            "  Exemplos (produtos da loja sem cadastro no site): " + string.Join(", ", longos))!;
        Assert.EndsWith("…", linha);

        var resumo = ResumoEstoqueSiteLeitor.Ler(["Recebidos: 10", "Atualizados: 0", "SKUs desconhecidos: 10", linha]);

        Assert.NotNull(resumo);
        Assert.NotEmpty(resumo.AmostraSemCadastro);
        Assert.All(resumo.AmostraSemCadastro, codigo => Assert.Contains(codigo, longos));
    }
}
