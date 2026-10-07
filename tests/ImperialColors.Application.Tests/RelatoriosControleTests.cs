using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Helpers;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Domain.ReadModels;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Os dois relatórios de controle que o lojista tira período a período: o que cada canal
/// vendeu de cada produto, e o extrato de entrada e saída de cada item.
/// </summary>
public class RelatoriosControleTests
{
    private readonly Mock<IRelatorioAnalyticsRepository> _repository = new();

    private RelatorioAnalyticsService CriarServico() => new(
        _repository.Object,
        new Mock<IVendaService>().Object,
        new Mock<IVendaExternaService>().Object);

    private static LinhaVendaPorCanalResumo Linha(
        CanalVenda canal, string codigo = "P001", decimal valor = 100m,
        decimal quantidade = 1m, bool cadastrado = true) => new()
    {
        DataVenda = new DateTime(2026, 10, 1, 10, 0, 0),
        Canal = canal,
        CodigoProduto = codigo,
        NomeProduto = "Tinta Acrílica 18L",
        NumeroVenda = "000001",
        Quantidade = quantidade,
        ValorUnitario = valor,
        ValorTotal = valor * quantidade,
        ProdutoCadastrado = cadastrado
    };

    private void ConfigurarVendas(params LinhaVendaPorCanalResumo[] linhas)
        => _repository
            .Setup(r => r.ObterVendasPorCanalAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(linhas);

    // ---------- Vendas por canal ----------

    [Fact]
    public async Task VendasPorCanal_LevaOCanalDeCadaVendaParaORelatorio()
    {
        ConfigurarVendas(
            Linha(CanalVenda.LojaFisica),
            Linha(CanalVenda.Site),
            Linha(CanalVenda.VendaExternaRua));

        var linhas = await CriarServico().ObterVendasPorCanalAsync(DateTime.Today, DateTime.Today);

        Assert.Equal(
            ["Loja física", "Site", "Venda externa (Rua)"],
            linhas.Select(l => l.CanalDescricao).OrderBy(d => d).ToArray());
    }

    /// <summary>
    /// Canal que não vendeu no período precisa aparecer zerado. Sumindo da lista, o lojista
    /// teria que reparar na ausência para concluir que o site não vendeu nada — e ausência
    /// não se lê.
    /// </summary>
    [Fact]
    public void TotaisPorCanal_CanalSemVendaNoPeriodoApareceZerado()
    {
        var servico = CriarServico();

        var totais = servico.TotalizarPorCanal([
            new LinhaVendaPorCanalDto { Canal = CanalVenda.LojaFisica, Quantidade = 2m, ValorTotal = 200m }
        ]);

        var site = totais.Single(t => t.Canal == CanalVenda.Site);
        Assert.Equal(0m, site.ValorTotal);
        Assert.Equal(0, site.QuantidadeLinhas);
    }

    [Fact]
    public void TotaisPorCanal_SomamValorEQuantidadeDeCadaCanal()
    {
        var servico = CriarServico();

        var totais = servico.TotalizarPorCanal([
            new LinhaVendaPorCanalDto { Canal = CanalVenda.Site, Quantidade = 2m, ValorTotal = 150m },
            new LinhaVendaPorCanalDto { Canal = CanalVenda.Site, Quantidade = 3m, ValorTotal = 90m },
            new LinhaVendaPorCanalDto { Canal = CanalVenda.LojaFisica, Quantidade = 1m, ValorTotal = 60m }
        ]);

        var site = totais.Single(t => t.Canal == CanalVenda.Site);
        Assert.Equal(240m, site.ValorTotal);
        Assert.Equal(5m, site.QuantidadeItens);
        Assert.Equal(2, site.QuantidadeLinhas);
    }

    /// <summary>A ordem é fixa e não depende do que apareceu no período: dois relatórios de
    /// meses diferentes têm que poder ser comparados linha a linha.</summary>
    [Fact]
    public void TotaisPorCanal_SaemSempreNaMesmaOrdem()
    {
        var servico = CriarServico();

        var totais = servico.TotalizarPorCanal([
            new LinhaVendaPorCanalDto { Canal = CanalVenda.Site, ValorTotal = 10m },
            new LinhaVendaPorCanalDto { Canal = CanalVenda.LojaFisica, ValorTotal = 20m }
        ]);

        Assert.Equal(
            [CanalVenda.LojaFisica, CanalVenda.VendaExternaRua, CanalVenda.Site],
            totais.Select(t => t.Canal).ToArray());
    }

    /// <summary>
    /// Item digitado à mão na venda externa entra no faturamento mas não tem produto
    /// cadastrado atrás. A célula de código não pode sair vazia: numa planilha, vazio se lê
    /// como falha na geração do arquivo.
    /// </summary>
    [Fact]
    public async Task ItemManualSemProduto_SaiMarcadoEmVezDeComCodigoEmBranco()
    {
        ConfigurarVendas(Linha(CanalVenda.VendaExternaRua, codigo: string.Empty, cadastrado: false));

        var linha = (await CriarServico().ObterVendasPorCanalAsync(DateTime.Today, DateTime.Today)).Single();

        Assert.False(linha.ProdutoCadastrado);
        Assert.Equal("(sem cadastro)", linha.CodigoExibicao);
    }

    // ---------- Movimentação de produtos ----------

    private void ConfigurarMovimentacoes(params LinhaMovimentacaoProdutoResumo[] linhas)
        => _repository
            .Setup(r => r.ObterMovimentacoesProdutosAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(linhas);

    [Fact]
    public async Task Movimentacao_EntradaESaidaSaemDescritasEmPortugues()
    {
        ConfigurarMovimentacoes(
            new LinhaMovimentacaoProdutoResumo { Tipo = TipoMovimentacao.Entrada, Motivo = "Estoque inicial" },
            new LinhaMovimentacaoProdutoResumo { Tipo = TipoMovimentacao.Saida, Motivo = "Venda" },
            new LinhaMovimentacaoProdutoResumo { Tipo = TipoMovimentacao.Ajuste, Motivo = "Inventário" });

        var linhas = await CriarServico().ObterMovimentacoesProdutosAsync(DateTime.Today, DateTime.Today);

        Assert.Equal(["Entrada", "Saída", "Ajuste"], linhas.Select(l => l.TipoDescricao).ToArray());
    }

    /// <summary>
    /// A coluna com sinal existe para a planilha somar e dar o saldo movimentado no período
    /// direto. Saída tem que entrar negativa, senão a soma devolve o total movimentado e não
    /// o que sobrou.
    /// </summary>
    [Fact]
    public async Task Movimentacao_SaidaEntraNegativaParaAColunaPoderSerSomada()
    {
        ConfigurarMovimentacoes(
            new LinhaMovimentacaoProdutoResumo { Tipo = TipoMovimentacao.Entrada, Quantidade = 10m },
            new LinhaMovimentacaoProdutoResumo { Tipo = TipoMovimentacao.Saida, Quantidade = 4m });

        var linhas = await CriarServico().ObterMovimentacoesProdutosAsync(DateTime.Today, DateTime.Today);

        Assert.Equal(6m, linhas.Sum(l => l.QuantidadeComSinal));
    }

    /// <summary>Saída de venda precisa dizer de qual venda e por qual canal — "saída de 2 un"
    /// sozinho não permite conferir nada.</summary>
    [Fact]
    public async Task Movimentacao_SaidaDeVendaMostraODocumentoEOCanal()
    {
        ConfigurarMovimentacoes(new LinhaMovimentacaoProdutoResumo
        {
            Tipo = TipoMovimentacao.Saida,
            NumeroVenda = "20260918-0001",
            Canal = CanalVenda.Site,
            Motivo = "Venda"
        });

        var linha = (await CriarServico().ObterMovimentacoesProdutosAsync(DateTime.Today, DateTime.Today)).Single();

        Assert.Equal("20260918-0001 — Site", linha.OrigemDescricao);
    }

    /// <summary>Entrada de compra não tem venda por trás: aí o motivo é a única explicação
    /// que existe, e é ele que a coluna tem que mostrar.</summary>
    [Fact]
    public async Task Movimentacao_EntradaSemVendaMostraOMotivo()
    {
        ConfigurarMovimentacoes(new LinhaMovimentacaoProdutoResumo
        {
            Tipo = TipoMovimentacao.Entrada,
            Motivo = "Estoque inicial"
        });

        var linha = (await CriarServico().ObterMovimentacoesProdutosAsync(DateTime.Today, DateTime.Today)).Single();

        Assert.Equal("Estoque inicial", linha.OrigemDescricao);
    }

    // ---------- Arquivamento ----------

    /// <summary>A cópia vai para {raiz}\{mes-ano}\{dd-MM-yyyy}, igual ao backup: quem já
    /// sabe achar um backup acha o relatório sem precisar perguntar.</summary>
    [Fact]
    public void Arquivamento_GuardaACopiaNaPastaDoDia()
    {
        var raiz = Path.Combine(Path.GetTempPath(), $"relat_{Guid.NewGuid():N}");
        var origem = Path.Combine(Path.GetTempPath(), $"VendasPorCanal_{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(origem, "conteudo");

        try
        {
            var destino = RelatorioArquivoHelper.ArquivarCopia(origem, new DateTime(2026, 10, 7), raiz);

            Assert.NotNull(destino);
            Assert.True(File.Exists(destino));
            Assert.Contains(Path.Combine("outubro-2026", "07-10-2026"), destino);
            // O arquivo que o operador escolheu continua onde estava.
            Assert.True(File.Exists(origem));
        }
        finally
        {
            File.Delete(origem);
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }

    /// <summary>
    /// Arquivamento é conveniência, não requisito: pasta sem permissão ou disco cheio não
    /// podem derrubar a geração de um relatório que o operador já salvou onde queria.
    /// </summary>
    [Fact]
    public void Arquivamento_ArquivoInexistente_DevolveNuloEmVezDeEstourar()
    {
        var inexistente = Path.Combine(Path.GetTempPath(), $"nao_existe_{Guid.NewGuid():N}.pdf");

        Assert.Null(RelatorioArquivoHelper.ArquivarCopia(inexistente, DateTime.Today));
    }

    /// <summary>Gerou duas vezes no mesmo dia: a segunda substitui a primeira, em vez de
    /// encher a pasta de versões que ninguém distingue.</summary>
    [Fact]
    public void Arquivamento_MesmoRelatorioNoMesmoDia_SubstituiACopiaAnterior()
    {
        var raiz = Path.Combine(Path.GetTempPath(), $"relat_{Guid.NewGuid():N}");
        var origem = Path.Combine(Path.GetTempPath(), $"MovimentacaoProdutos_{Guid.NewGuid():N}.xlsx");

        try
        {
            File.WriteAllText(origem, "primeira");
            RelatorioArquivoHelper.ArquivarCopia(origem, new DateTime(2026, 10, 7), raiz);

            File.WriteAllText(origem, "segunda");
            var destino = RelatorioArquivoHelper.ArquivarCopia(origem, new DateTime(2026, 10, 7), raiz);

            Assert.Equal("segunda", File.ReadAllText(destino!));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(destino)!));
        }
        finally
        {
            File.Delete(origem);
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }

    // ---------- Canal ----------

    /// <summary>Os três canais precisam ter texto próprio: no relatório, o nome do canal é a
    /// única coisa que separa uma venda da outra.</summary>
    [Fact]
    public void CadaCanalTemNomeProprio()
    {
        var nomes = CanalVendaHelper.OrdemRelatorio.Select(CanalVendaHelper.Descricao).ToList();

        Assert.Equal(["Loja física", "Venda externa (Rua)", "Site"], nomes);
        Assert.Equal(nomes.Count, nomes.Distinct().Count());
    }

    /// <summary>
    /// Os números do enum são gravados no banco. Mudá-los remontaria o histórico: venda
    /// marcada como site passaria a aparecer como rua sem ninguém ter editado nada.
    /// </summary>
    [Fact]
    public void ValoresDoCanal_NaoPodemMudar()
    {
        Assert.Equal(1, (int)CanalVenda.LojaFisica);
        Assert.Equal(2, (int)CanalVenda.VendaExternaRua);
        Assert.Equal(3, (int)CanalVenda.Site);
    }
}
