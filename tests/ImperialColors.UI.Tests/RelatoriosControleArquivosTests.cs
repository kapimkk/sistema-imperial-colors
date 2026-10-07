using ClosedXML.Excel;
using ImperialColors.Application.Configuration;
using ImperialColors.Application.DTOs;
using ImperialColors.Domain.Enums;
using ImperialColors.UI.Services;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// Geração dos arquivos dos dois relatórios de controle. O PDF é para conferir na mão; a
/// planilha é para filtrar e somar — e é por isso que ela leva data e valor como número, e
/// não como texto formatado. Os testes travam essa diferença, que some em silêncio.
/// </summary>
public class RelatoriosControleArquivosTests : IDisposable
{
    private readonly string _pastaTemp = Path.Combine(
        Path.GetTempPath(), "ImperialColorsTests", Guid.NewGuid().ToString("N"));

    public RelatoriosControleArquivosTests()
    {
        Directory.CreateDirectory(_pastaTemp);
        File.WriteAllText(Path.Combine(_pastaTemp, "appsettings.json"),
            """
            { "DadosEmpresa": { "NomeFantasia": "Imperial Colors" } }
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_pastaTemp, recursive: true); } catch (IOException) { /* pasta temporária */ }
        GC.SuppressFinalize(this);
    }

    private IRelatorioService CriarRelatorioService(out ServiceProvider provider)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(_pastaTemp)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions<EmpresaConfig>().Bind(configuration.GetSection(EmpresaConfig.Secao));
        services.PostConfigure<EmpresaConfig>(EmpresaConfigEnvironmentOverrides.Aplicar);
        services.AddSingleton<IAppConfigService, AppConfigService>();
        services.AddSingleton<IRelatorioService, RelatorioService>();

        provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IRelatorioService>();
    }

    private static readonly DateTime Inicio = new(2026, 10, 1);
    private static readonly DateTime Fim = new(2026, 10, 31);

    private static IReadOnlyList<LinhaVendaPorCanalDto> Vendas() =>
    [
        new()
        {
            DataVenda = new DateTime(2026, 10, 3, 14, 30, 0),
            Canal = CanalVenda.LojaFisica,
            CodigoProduto = "TAB-001",
            NomeProduto = "Tinta Acrilica Branca",
            NumeroVenda = "000045",
            Quantidade = 2m,
            ValorUnitario = 189.90m,
            ValorTotal = 379.80m,
            ProdutoCadastrado = true
        },
        new()
        {
            DataVenda = new DateTime(2026, 10, 5, 9, 15, 0),
            Canal = CanalVenda.Site,
            CodigoProduto = "TAB-001",
            NomeProduto = "Tinta Acrilica Branca",
            NumeroVenda = "VE-0009",
            Quantidade = 1m,
            ValorUnitario = 199.90m,
            ValorTotal = 199.90m,
            ProdutoCadastrado = true
        },
        new()
        {
            DataVenda = new DateTime(2026, 10, 6, 11, 0, 0),
            Canal = CanalVenda.VendaExternaRua,
            CodigoProduto = string.Empty,
            NomeProduto = "Lixa avulsa",
            NumeroVenda = "VE-0010",
            Quantidade = 5m,
            ValorUnitario = 3m,
            ValorTotal = 15m,
            ProdutoCadastrado = false
        }
    ];

    private static IReadOnlyList<TotalCanalDto> Totais() =>
    [
        new() { Canal = CanalVenda.LojaFisica, QuantidadeLinhas = 1, QuantidadeItens = 2m, ValorTotal = 379.80m },
        new() { Canal = CanalVenda.VendaExternaRua, QuantidadeLinhas = 1, QuantidadeItens = 5m, ValorTotal = 15m },
        new() { Canal = CanalVenda.Site, QuantidadeLinhas = 1, QuantidadeItens = 1m, ValorTotal = 199.90m }
    ];

    private static IReadOnlyList<LinhaMovimentacaoProdutoDto> Movimentacoes() =>
    [
        new()
        {
            Data = new DateTime(2026, 10, 1, 8, 0, 0),
            CodigoProduto = "TAB-001",
            NomeProduto = "Tinta Acrilica Branca",
            Unidade = "GL",
            Tipo = TipoMovimentacao.Entrada,
            Quantidade = 20m,
            SaldoAnterior = 0m,
            SaldoPosterior = 20m,
            Motivo = "Estoque inicial"
        },
        new()
        {
            Data = new DateTime(2026, 10, 3, 14, 30, 0),
            CodigoProduto = "TAB-001",
            NomeProduto = "Tinta Acrilica Branca",
            Unidade = "GL",
            Tipo = TipoMovimentacao.Saida,
            Quantidade = 2m,
            SaldoAnterior = 20m,
            SaldoPosterior = 18m,
            Motivo = "Venda",
            NumeroVenda = "000045",
            Canal = CanalVenda.LojaFisica
        }
    ];

    // ---------- Vendas por Canal ----------

    [Fact]
    public async Task VendasPorCanal_Pdf_TrazOsCanaisEOsTotais()
    {
        var caminho = Path.Combine(_pastaTemp, "canal.pdf");
        var relatorio = CriarRelatorioService(out var provider);
        await using (provider)
            await relatorio.GerarRelatorioVendasPorCanalPdfAsync(Vendas(), Totais(), Inicio, Fim, caminho);

        var texto = ExtrairTextoPdf(caminho);

        Assert.Contains("Vendas por Canal e Produto", texto);
        Assert.Contains("Loja f", texto);
        Assert.Contains("Site", texto);
        Assert.Contains("TAB-001", texto);
        Assert.Contains("Totais por canal", texto);
        Assert.Contains("R$ 379,80", texto);
    }

    /// <summary>Item sem produto cadastrado precisa sair marcado — célula de código vazia na
    /// planilha se lê como falha na geração do arquivo.</summary>
    [Fact]
    public async Task VendasPorCanal_ItemManual_SaiMarcadoNoArquivo()
    {
        var caminho = Path.Combine(_pastaTemp, "canal-manual.pdf");
        var relatorio = CriarRelatorioService(out var provider);
        await using (provider)
            await relatorio.GerarRelatorioVendasPorCanalPdfAsync(Vendas(), Totais(), Inicio, Fim, caminho);

        Assert.Contains("(sem cadastro)", ExtrairTextoPdf(caminho));
    }

    /// <summary>
    /// A planilha existe para o lojista filtrar por canal e somar. Isso só funciona com data
    /// como data e valor como número: texto formatado quebra o filtro e a soma em silêncio.
    /// </summary>
    [Fact]
    public async Task VendasPorCanal_Excel_LevaDataEValorComoNumeroSomavel()
    {
        var caminho = Path.Combine(_pastaTemp, "canal.xlsx");
        var relatorio = CriarRelatorioService(out var provider);
        await using (provider)
            await relatorio.GerarRelatorioVendasPorCanalExcelAsync(Vendas(), Totais(), Inicio, Fim, caminho);

        using var workbook = new XLWorkbook(caminho);
        var ws = workbook.Worksheet("Vendas por Canal");

        Assert.Equal(new DateTime(2026, 10, 3, 14, 30, 0), ws.Cell(5, 1).GetDateTime());
        Assert.Equal("Loja física", ws.Cell(5, 2).GetString());
        Assert.Equal(379.80m, ws.Cell(5, 8).GetValue<decimal>());
        Assert.True(ws.AutoFilter.IsEnabled, "sem autofiltro, separar por canal vira trabalho manual");
    }

    /// <summary>Os totais vão numa aba própria: misturados às linhas, entrariam no filtro e
    /// seriam somados junto com elas.</summary>
    [Fact]
    public async Task VendasPorCanal_Excel_TemAbaSeparadaDeTotais()
    {
        var caminho = Path.Combine(_pastaTemp, "canal-totais.xlsx");
        var relatorio = CriarRelatorioService(out var provider);
        await using (provider)
            await relatorio.GerarRelatorioVendasPorCanalExcelAsync(Vendas(), Totais(), Inicio, Fim, caminho);

        using var workbook = new XLWorkbook(caminho);
        var ws = workbook.Worksheet("Totais por Canal");

        Assert.Equal("Loja física", ws.Cell(2, 1).GetString());
        Assert.Equal(379.80m, ws.Cell(2, 4).GetValue<decimal>());
    }

    // ---------- Movimentação de Produtos ----------

    [Fact]
    public async Task Movimentacao_Pdf_MostraEntradaSaidaEOrigem()
    {
        var caminho = Path.Combine(_pastaTemp, "mov.pdf");
        var relatorio = CriarRelatorioService(out var provider);
        await using (provider)
            await relatorio.GerarRelatorioMovimentacaoProdutosPdfAsync(Movimentacoes(), Inicio, Fim, caminho);

        var texto = ExtrairTextoPdf(caminho);

        Assert.Contains("Movimentacao de Produtos", texto);
        Assert.Contains("TAB-001", texto);
        Assert.Contains("Entrada", texto);
        Assert.Contains("Estoque inicial", texto);
        Assert.Contains("000045", texto);
    }

    /// <summary>
    /// Saída entra negativa na coluna com sinal para a planilha dar o saldo movimentado
    /// numa soma só: 20 de entrada e 2 de saída têm que fechar em 18.
    /// </summary>
    [Fact]
    public async Task Movimentacao_Excel_ColunaComSinalFechaOSaldoDoPeriodo()
    {
        var caminho = Path.Combine(_pastaTemp, "mov.xlsx");
        var relatorio = CriarRelatorioService(out var provider);
        await using (provider)
            await relatorio.GerarRelatorioMovimentacaoProdutosExcelAsync(Movimentacoes(), Inicio, Fim, caminho);

        using var workbook = new XLWorkbook(caminho);
        var ws = workbook.Worksheet("Movimentacoes");

        Assert.Equal(20m, ws.Cell(5, 7).GetValue<decimal>());
        Assert.Equal(-2m, ws.Cell(6, 7).GetValue<decimal>());
        Assert.Equal(18m, ws.Cell(5, 7).GetValue<decimal>() + ws.Cell(6, 7).GetValue<decimal>());
    }

    [Fact]
    public async Task Movimentacao_Excel_SaidaDeVendaMostraODocumentoEOCanal()
    {
        var caminho = Path.Combine(_pastaTemp, "mov-origem.xlsx");
        var relatorio = CriarRelatorioService(out var provider);
        await using (provider)
            await relatorio.GerarRelatorioMovimentacaoProdutosExcelAsync(Movimentacoes(), Inicio, Fim, caminho);

        using var workbook = new XLWorkbook(caminho);
        var ws = workbook.Worksheet("Movimentacoes");

        Assert.Equal("Estoque inicial", ws.Cell(5, 10).GetString());
        Assert.Equal("000045 — Loja física", ws.Cell(6, 10).GetString());
    }

    /// <summary>Período sem movimento nenhum tem que gerar o arquivo com cabeçalho, não
    /// estourar: "não houve movimentação" é uma resposta legítima.</summary>
    [Fact]
    public async Task Movimentacao_PeriodoSemMovimento_GeraArquivoVazioSemErro()
    {
        var caminho = Path.Combine(_pastaTemp, "mov-vazio.xlsx");
        var relatorio = CriarRelatorioService(out var provider);
        await using (provider)
            await relatorio.GerarRelatorioMovimentacaoProdutosExcelAsync([], Inicio, Fim, caminho);

        using var workbook = new XLWorkbook(caminho);
        Assert.Equal("Data", workbook.Worksheet("Movimentacoes").Cell(4, 1).GetString());
    }

    private static string ExtrairTextoPdf(string caminho)
    {
        using var reader = new PdfReader(caminho);
        using var pdf = new PdfDocument(reader);
        var texto = new System.Text.StringBuilder();
        for (var pagina = 1; pagina <= pdf.GetNumberOfPages(); pagina++)
            texto.AppendLine(PdfTextExtractor.GetTextFromPage(pdf.GetPage(pagina), new SimpleTextExtractionStrategy()));
        return texto.ToString();
    }
}
