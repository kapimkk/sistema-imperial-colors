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
    // 14 e 15: o estoque foi enviado, mas há produto da loja sem cadastro no site (alerta, não falha).
    [InlineData(14, StatusSincronizacaoSite.ConcluidaComAtencao)]
    [InlineData(15, StatusSincronizacaoSite.ConcluidaComAtencao)]
    [InlineData(16, StatusSincronizacaoSite.Falhou)]
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

    // ---------------------------------------------------------------------------------------
    // Código de saída + resumo do estoque: "a API aceitou" não é "o estoque foi sincronizado"
    // ---------------------------------------------------------------------------------------

    private static ResumoEstoqueSite Resumo(int recebidos, int atualizados, int semCadastro, params string[] amostra) => new()
    {
        Recebidos = recebidos,
        Atualizados = atualizados,
        SemCadastro = semCadastro,
        AmostraSemCadastro = amostra
    };

    [Theory]
    [InlineData(0)]   // ImperialSync antigo: terminava com 0 mesmo sem reconhecer nenhum produto
    [InlineData(14)]  // ImperialSync atual: código próprio do alerta
    public void Recebidos222_Atualizados0_SemCadastro222_EhAlerta_NuncaSucesso(int codigo)
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.Interpretar(codigo, Resumo(222, 0, 222));

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, status);
        Assert.Equal(
            "Sincronização concluída com alerta. 222 produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site.",
            mensagem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void Recebidos222_Atualizados200_SemCadastro22_EhAlertaParcial(int codigo)
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.Interpretar(codigo, Resumo(222, 200, 22));

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, status);
        Assert.Equal(
            "Sincronização concluída com alerta. 22 dos 222 produtos enviados não têm cadastro no catálogo do site.",
            mensagem);
    }

    [Fact]
    public void Recebidos222_TodosReconhecidos_EhSucesso()
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.Interpretar(0, Resumo(222, 222, 0));

        Assert.Equal(StatusSincronizacaoSite.Concluida, status);
        Assert.Equal("Sincronização concluída com sucesso.", mensagem);
    }

    [Fact]
    public void ReconhecidosEJaEmDiaNoSite_NadaAtualizado_TambemEhSucesso()
    {
        // O site conhece todos e já tinha a leitura: nada a gravar e nada a alertar.
        var (status, _) = SincronizacaoSiteMensagens.Interpretar(0, Resumo(222, 0, 0));

        Assert.Equal(StatusSincronizacaoSite.Concluida, status);
    }

    [Fact]
    public void CodigoZeroSemResumo_ContinuaSendoSucesso()
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.Interpretar(0, null);

        Assert.Equal(StatusSincronizacaoSite.Concluida, status);
        Assert.Equal(SincronizacaoSiteMensagens.Concluida, mensagem);
    }

    [Theory]
    [InlineData(14, "nenhum SKU foi encontrado no catálogo do site")]
    [InlineData(15, "produtos da loja sem cadastro no catálogo do site")]
    public void CodigoDeAlertaSemOsNumeros_EhAlertaDoMesmoJeito(int codigo, string trecho)
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.Interpretar(codigo, null);

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, status);
        Assert.StartsWith("Sincronização concluída com alerta.", mensagem);
        Assert.Contains(trecho, mensagem);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(15)]
    public void CodigoDeAlertaComResumoSemDesconhecidos_ContinuaAlerta(int codigo)
    {
        // Código e resumo discordam: vale o mais cauteloso. Verde, só com os dois de acordo.
        var (status, _) = SincronizacaoSiteMensagens.Interpretar(codigo, Resumo(222, 222, 0));

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, status);
    }

    [Theory]
    [InlineData(1)]   // erro inesperado
    [InlineData(2)]   // configuração
    [InlineData(4)]   // banco da loja
    [InlineData(5)]   // API recusou (assinatura/HMAC, 401, 403)
    [InlineData(6)]   // API indisponível (HTTP)
    [InlineData(7)]   // snapshot não confirmado
    [InlineData(11)]  // integração ausente
    [InlineData(12)]  // resposta sem assinatura válida
    [InlineData(77)]  // código que este sistema não conhece
    public void FalhaTecnica_EhErro_MesmoComProdutoSemCadastroNoResumo(int codigo)
    {
        var semResumo = SincronizacaoSiteMensagens.InterpretarCodigoSaida(codigo);

        var comResumo = SincronizacaoSiteMensagens.Interpretar(codigo, Resumo(222, 0, 222));

        Assert.Equal(StatusSincronizacaoSite.Falhou, comResumo.Status);
        // A mensagem é a da falha: o alerta do estoque não a substitui nem a suaviza.
        Assert.Equal(semResumo.Mensagem, comResumo.Mensagem);
        Assert.DoesNotContain("concluída", comResumo.Mensagem);
    }

    [Theory]
    [InlineData(3, StatusSincronizacaoSite.JaEmExecucao)]
    [InlineData(13, StatusSincronizacaoSite.JaEmExecucao)]
    [InlineData(9, StatusSincronizacaoSite.Cancelada)]
    public void ExecucaoQueNaoRodouOuFoiInterrompida_NaoViraAlertaDeEstoque(int codigo, StatusSincronizacaoSite esperado)
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.Interpretar(codigo, Resumo(222, 0, 222));

        Assert.Equal(esperado, status);
        Assert.Equal(SincronizacaoSiteMensagens.InterpretarCodigoSaida(codigo).Mensagem, mensagem);
    }

    [Fact]
    public void VendasComAtencaoMaisProdutoSemCadastro_MostraOsDoisAlertas()
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.Interpretar(10, Resumo(222, 200, 22));

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, status);
        Assert.Contains("vendas do site que precisam de atenção", mensagem);
        Assert.EndsWith("Além disso, 22 dos 222 produtos enviados não têm cadastro no catálogo do site.", mensagem);
    }

    [Fact]
    public void VendasComAtencaoETodoOEstoqueReconhecido_SoOAlertaDasVendas()
    {
        var (status, mensagem) = SincronizacaoSiteMensagens.Interpretar(10, Resumo(222, 222, 0));

        Assert.Equal(StatusSincronizacaoSite.ConcluidaComAtencao, status);
        Assert.Equal(SincronizacaoSiteMensagens.InterpretarCodigoSaida(10).Mensagem, mensagem);
    }

    [Fact]
    public void SoEhSucesso_ComCodigoZero_ESemNenhumProdutoSemCadastro()
    {
        ResumoEstoqueSite?[] resumos = [null, Resumo(222, 222, 0), Resumo(222, 0, 0), Resumo(222, 200, 22), Resumo(222, 0, 222), Resumo(1, 0, 1)];

        var sucessos = (
            from codigo in Enumerable.Range(-5, 200)
            from resumo in resumos
            where SincronizacaoSiteMensagens.Interpretar(codigo, resumo).Status == StatusSincronizacaoSite.Concluida
            select (codigo, semCadastro: resumo?.SemCadastro ?? 0)).ToList();

        Assert.NotEmpty(sucessos);
        Assert.All(sucessos, sucesso => Assert.Equal((0, 0), sucesso));
        // null, 222/222/0 e 222/0/0: as três formas de "nada ficou sem cadastro".
        Assert.Equal(3, sucessos.Count);
    }

    [Theory]
    [InlineData(1, 0, 1, "1 produto foi enviado, mas o SKU dele não foi encontrado no catálogo do site.")]
    [InlineData(222, 221, 1, "1 dos 222 produtos enviados não tem cadastro no catálogo do site.")]
    [InlineData(1209, 0, 1209, "1.209 produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site.")]
    [InlineData(1209, 209, 1000, "1.000 dos 1.209 produtos enviados não têm cadastro no catálogo do site.")]
    [InlineData(5, 0, 9, "5 produtos foram enviados, mas nenhum SKU foi encontrado no catálogo do site.")]
    // Nada foi gravado, mas 22 produtos SÃO do catálogo (já estavam em dia): o alerta é parcial, não "nenhum".
    [InlineData(222, 0, 200, "200 dos 222 produtos enviados não têm cadastro no catálogo do site.")]
    public void FraseDoAlerta_ConcordaEmNumero_EUsaSeparadorDeMilhar(int recebidos, int atualizados, int semCadastro, string esperado)
    {
        Assert.Equal(esperado, SincronizacaoSiteMensagens.DescreverSemCadastro(Resumo(recebidos, atualizados, semCadastro)));
    }

    [Fact]
    public void LinhaDoResumo_TrazRecebidosAtualizadosESemCadastro()
    {
        Assert.Equal(
            "Recebidos pelo site: 222  ·  Atualizados: 0  ·  SKUs sem cadastro: 222",
            SincronizacaoSiteMensagens.DescreverResumo(Resumo(222, 0, 222)));
        Assert.Equal(
            "Recebidos pelo site: 1.209  ·  Atualizados: 1.200  ·  SKUs sem cadastro: 9",
            SincronizacaoSiteMensagens.DescreverResumo(Resumo(1209, 1200, 9)));
    }

    [Fact]
    public void Amostra_DizQuantosDeQuantos_EFicaVaziaSemDesconhecidos()
    {
        Assert.Equal(
            "Exemplos de SKUs sem cadastro (3 de 222): 21201050, 301010001, DIL001",
            SincronizacaoSiteMensagens.DescreverAmostra(Resumo(222, 0, 222, "21201050", "301010001", "DIL001")));
        // Quando a amostra é a lista inteira, não há "de quantos".
        Assert.Equal(
            "Exemplos de SKUs sem cadastro (2): A-1, B-2",
            SincronizacaoSiteMensagens.DescreverAmostra(Resumo(222, 220, 2, "A-1", "B-2")));
        Assert.Equal(string.Empty, SincronizacaoSiteMensagens.DescreverAmostra(Resumo(222, 0, 222)));
        Assert.Equal(string.Empty, SincronizacaoSiteMensagens.DescreverAmostra(Resumo(222, 222, 0, "SOBRA-1")));
    }

    [Theory]
    [InlineData(0, 222, 0, 222, true)]
    [InlineData(0, 222, 200, 22, true)]
    [InlineData(0, 222, 222, 0, false)]
    [InlineData(10, 222, 200, 22, true)]
    [InlineData(6, 222, 222, 0, false)]
    public void Orientacao_SoQuandoHaProdutoSemCadastro(int codigo, int recebidos, int atualizados, int semCadastro, bool esperado)
    {
        Assert.Equal(esperado, SincronizacaoSiteMensagens.HaProdutoSemCadastro(codigo, Resumo(recebidos, atualizados, semCadastro)));
    }

    [Fact]
    public void Orientacao_TambemPeloCodigoDeAlerta_MesmoSemResumo()
    {
        Assert.True(SincronizacaoSiteMensagens.HaProdutoSemCadastro(14, null));
        Assert.True(SincronizacaoSiteMensagens.HaProdutoSemCadastro(15, null));
        Assert.False(SincronizacaoSiteMensagens.HaProdutoSemCadastro(0, null));
        Assert.False(SincronizacaoSiteMensagens.HaProdutoSemCadastro(10, null));
        Assert.False(SincronizacaoSiteMensagens.HaProdutoSemCadastro(null, null));
        Assert.Contains("Código do Produto", SincronizacaoSiteMensagens.OrientacaoSemCadastro);
    }
}
