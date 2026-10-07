using ClosedXML.Excel;

using ImperialColors.Application.Configuration;
using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Domain.Enums;
using ImperialColors.UI.Helpers;

using iText.IO.Font.Constants;

using iText.Kernel.Colors;

using iText.Kernel.Font;

using iText.Kernel.Pdf;

using iText.Layout;

using iText.Layout.Element;

using iText.Layout.Properties;

using System.IO;



namespace ImperialColors.UI.Services;



using ITextParagraph = iText.Layout.Element.Paragraph;

using ITextTable = iText.Layout.Element.Table;

using ITextCell = iText.Layout.Element.Cell;



public class RelatorioService : IRelatorioService

{

    private readonly IAppConfigService _config;



    public RelatorioService(IAppConfigService config)

    {

        _config = config;

    }



    // Uma fonte só para os dados da empresa: assim um cadastro editado em Configurações já sai
    // certo no próximo cupom/relatório, sem reiniciar o sistema.
    private EmpresaConfig Empresa => _config.Empresa;



    private static PdfFont ObterFonte(bool negrito = false)

        => PdfFontFactory.CreateFont(negrito ? StandardFonts.HELVETICA_BOLD : StandardFonts.HELVETICA);



    public Task GerarCupomPdfAsync(VendaDto venda, string caminhoArquivo)

    {

        return Task.Run(() =>

        {

            using var writer = new PdfWriter(caminhoArquivo);

            using var pdf = new PdfDocument(writer);

            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A6);

            document.SetMargins(16, 16, 16, 16);



            AdicionarCabecalhoCupom(document);



            AdicionarLinhaSeparadora(document);



            document.Add(CriarLinhaRotuloValor("Venda Nº:", venda.NumeroVenda).SetMarginBottom(5));

            document.Add(CriarLinhaRotuloValor("Data:", venda.DataVenda.ToString("dd/MM/yyyy HH:mm")).SetMarginBottom(8));



            AdicionarLinhaSeparadora(document);



            var cultura = new System.Globalization.CultureInfo("pt-BR");
            var tabela = new ITextTable(new float[] { 3.2f, 0.8f, 0.7f, 1.3f }).UseAllAvailableWidth();

            AdicionarCabecalhoTabela(tabela, "PRODUTO", "QTD", "UN", "VALOR");



            foreach (var item in venda.Itens)

            {

                tabela.AddCell(CelulaCupom(item.NomeProduto));

                tabela.AddCell(CelulaCupom(FormatarQuantidadeCupom(item.Quantidade), TextAlignment.RIGHT));

                tabela.AddCell(CelulaCupom(item.Unidade ?? "UN", TextAlignment.CENTER));

                tabela.AddCell(CelulaCupom(item.PrecoUnitario.ToString("C2", cultura), TextAlignment.RIGHT));

            }



            document.Add(tabela);

            AdicionarLinhaSeparadora(document);



            document.Add(CriarLinhaRotuloValor("Subtotal:", venda.Subtotal.ToString("C2", new System.Globalization.CultureInfo("pt-BR"))));



            if (venda.Desconto > 0)

                document.Add(CriarLinhaRotuloValor("Desconto:", venda.Desconto.ToString("C2", new System.Globalization.CultureInfo("pt-BR"))));

            var totalItens = venda.Itens.Sum(i => i.Quantidade);
            document.Add(CriarLinhaRotuloValor("Total de Itens:", FormatarQuantidadeCupom(totalItens)).SetMarginTop(4));

            document.Add(new ITextParagraph($"TOTAL: {venda.Total.ToString("C2", new System.Globalization.CultureInfo("pt-BR"))}")

                .SetFont(ObterFonte(true)).SetFontSize(13).SetTextAlignment(TextAlignment.RIGHT)

                .SetFontColor(ColorConstants.BLACK).SetMarginTop(4));



            AdicionarLinhaSeparadora(document);



            document.Add(new ITextParagraph("PAGAMENTO")

                .SetFont(ObterFonte(true)).SetFontSize(10).SetFontColor(ColorConstants.BLACK).SetMarginBottom(4));



            document.Add(CriarLinhaRotuloValor("Forma:", venda.FormaPagamentoDescricao));



            if (venda.FormaPagamento == FormaPagamento.Dinheiro)

            {

                document.Add(CriarLinhaRotuloValor("Valor Recebido:", venda.ValorPago.ToString("C2", new System.Globalization.CultureInfo("pt-BR"))));

                document.Add(CriarLinhaRotuloValor("Troco:", venda.Troco.ToString("C2", new System.Globalization.CultureInfo("pt-BR"))));

            }



            AdicionarLinhaSeparadora(document);

            document.Add(new ITextParagraph(_config.CupomRodape)

                .SetFont(ObterFonte(true)).SetFontSize(9).SetTextAlignment(TextAlignment.CENTER)

                .SetFontColor(ColorConstants.BLACK));

        });

    }



    private void AdicionarCabecalhoCupom(Document document)

    {

        document.Add(new ITextParagraph(Empresa.NomeFantasia.ToUpperInvariant())

            .SetFont(ObterFonte(true)).SetFontColor(ColorConstants.BLACK).SetFontSize(13)

            .SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(2));



        if (!string.IsNullOrWhiteSpace(Empresa.RazaoSocial))

        {

            document.Add(new ITextParagraph(Empresa.RazaoSocial)

                .SetFont(ObterFonte(true)).SetFontSize(8).SetTextAlignment(TextAlignment.CENTER)

                .SetFontColor(ColorConstants.BLACK).SetMarginBottom(2));

        }



        if (!string.IsNullOrWhiteSpace(Empresa.CNPJ))

        {

            document.Add(new ITextParagraph($"CNPJ: {Empresa.CNPJ}")

                .SetFont(ObterFonte(true)).SetFontSize(8).SetTextAlignment(TextAlignment.CENTER)

                .SetFontColor(ColorConstants.BLACK).SetMarginBottom(1));

        }



        if (!string.IsNullOrWhiteSpace(Empresa.Endereco))

        {

            document.Add(new ITextParagraph(Empresa.Endereco)

                .SetFont(ObterFonte(true)).SetFontSize(8).SetTextAlignment(TextAlignment.CENTER)

                .SetFontColor(ColorConstants.BLACK).SetMarginBottom(1));

        }



        if (!string.IsNullOrWhiteSpace(Empresa.Telefone))

        {

            document.Add(new ITextParagraph($"Tel: {Empresa.Telefone}")

                .SetFont(ObterFonte(true)).SetFontSize(8).SetTextAlignment(TextAlignment.CENTER)

                .SetFontColor(ColorConstants.BLACK).SetMarginBottom(2));

        }



        document.Add(new ITextParagraph("CUPOM NÃO FISCAL")

            .SetFont(ObterFonte(true)).SetFontSize(9).SetTextAlignment(TextAlignment.CENTER)

            .SetFontColor(ColorConstants.BLACK).SetMarginBottom(2));

    }



    private static ITextParagraph CriarLinhaRotuloValor(string rotulo, string valor)

    {

        return new ITextParagraph()

            .Add(new Text(rotulo).SetFont(ObterFonte(true)).SetFontColor(ColorConstants.BLACK))

            .Add(new Text(" ").SetFont(ObterFonte(true)).SetFontColor(ColorConstants.BLACK))

            .Add(new Text(valor).SetFont(ObterFonte(true)).SetFontColor(ColorConstants.BLACK))

            .SetFontSize(9)

            .SetMarginBottom(3)

            .SetFixedLeading(12);

    }



    private static ITextCell CelulaCupom(string texto, TextAlignment alignment = TextAlignment.LEFT)

        => new ITextCell().Add(new ITextParagraph(texto).SetFont(ObterFonte(true)).SetFontSize(8)

                .SetTextAlignment(alignment).SetFontColor(ColorConstants.BLACK))

            .SetBorder(iText.Layout.Borders.Border.NO_BORDER)

            .SetBorderBottom(new iText.Layout.Borders.DottedBorder(ColorConstants.BLACK, 0.5f))

            .SetPadding(3);



    private static string FormatarQuantidadeCupom(decimal quantidade)

        => FormattingHelper.FormatarQuantidade(quantidade);



    public Task GerarRelatorioVendasPdfAsync(IEnumerable<VendaDto> vendas, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            AdicionarCabecalhoRelatorio(document, "Relatorio de Vendas",
                $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}");

            var tabela = new ITextTable(new float[] { 2, 2, 3, 1.5f, 1.5f, 1.5f, 1.5f }).UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "N Venda", "Data", "Cliente", "Subtotal", "Desconto", "Total", "Status");

            var lista = vendas.ToList();
            foreach (var venda in lista)
            {
                tabela.AddCell(CelulaTabela(venda.NumeroVenda));
                tabela.AddCell(CelulaTabela(venda.DataVenda.ToString("dd/MM/yy HH:mm")));
                tabela.AddCell(CelulaTabela(venda.ClienteNome ?? "Consumidor Final"));
                tabela.AddCell(CelulaTabela(venda.Subtotal.ToString("C2", new System.Globalization.CultureInfo("pt-BR")), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(venda.Desconto.ToString("C2", new System.Globalization.CultureInfo("pt-BR")), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(venda.Total.ToString("C2", new System.Globalization.CultureInfo("pt-BR")), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(venda.StatusDescricao));
            }

            document.Add(tabela);

            var total = lista.Sum(v => v.Total);
            document.Add(new ITextParagraph($"\nTotal do Periodo: {total.ToString("C2", new System.Globalization.CultureInfo("pt-BR"))}")
                .SetFont(ObterFonte(true)).SetFontSize(13).SetTextAlignment(TextAlignment.RIGHT));
        });
    }

    public Task GerarRelatorioEstoquePdfAsync(IEnumerable<ProdutoDto> produtos, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            AdicionarCabecalhoRelatorio(document, "Relatorio de Estoque", $"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}");

            var tabela = new ITextTable(new float[] { 1.4f, 2.8f, 1.8f, 1.3f, 1, 0.7f, 1.2f, 1.4f }).UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "Codigo", "Nome", "Categoria", "Marca", "Estoque", "Un", "Peso", "Preco");

            foreach (var produto in produtos)
            {
                tabela.AddCell(CelulaTabela(produto.CodigoInterno));
                tabela.AddCell(CelulaTabela(produto.Nome));
                tabela.AddCell(CelulaTabela(produto.CategoriaNome ?? "-"));
                tabela.AddCell(CelulaTabela(produto.MarcaNome ?? "-"));
                var celEstoque = CelulaTabela(produto.QuantidadeEstoque.ToString("G"), TextAlignment.RIGHT);
                if (produto.SemEstoque) celEstoque.SetFontColor(new DeviceRgb(220, 53, 69));
                else if (produto.EstoqueBaixo) celEstoque.SetFontColor(new DeviceRgb(253, 126, 20));
                tabela.AddCell(celEstoque);
                tabela.AddCell(CelulaTabela(produto.Unidade));
                // Peso legível ("5,5 kg", "800 g") — no PDF o valor é para conferir na mão,
                // não para somar; quem precisa de total usa a exportação em Excel, onde a
                // mesma coluna sai como número em quilos.
                var peso = produto.PesoFormatado;
                tabela.AddCell(CelulaTabela(peso.Length == 0 ? "-" : peso, TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(produto.PrecoVenda.ToString("C2", new System.Globalization.CultureInfo("pt-BR")), TextAlignment.RIGHT));
            }

            document.Add(tabela);
            document.Add(new ITextParagraph($"\nTotal de produtos: {produtos.Count()}").SetFont(ObterFonte()).SetFontSize(11));
        });
    }

    public Task GerarRelatorioVendasExcelAsync(IEnumerable<VendaDto> vendas, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Vendas");

            ws.Cell(1, 1).Value = $"{_config.EmpresaNome} - Relatorio de Vendas";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 7).Merge();

            ws.Cell(2, 1).Value = $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}";
            ws.Range(2, 1, 2, 7).Merge();

            var headers = new[] { "N Venda", "Data", "Cliente", "Subtotal", "Desconto", "Total", "Status" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            int row = 5;
            foreach (var venda in vendas)
            {
                ws.Cell(row, 1).Value = venda.NumeroVenda;
                ws.Cell(row, 2).Value = venda.DataVenda.ToString("dd/MM/yyyy HH:mm");
                ws.Cell(row, 3).Value = venda.ClienteNome ?? "Consumidor Final";
                ws.Cell(row, 4).Value = venda.Subtotal;
                ws.Cell(row, 4).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 5).Value = venda.Desconto;
                ws.Cell(row, 5).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 6).Value = venda.Total;
                ws.Cell(row, 6).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 7).Value = venda.StatusDescricao;
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(caminhoArquivo);
        });
    }

    public Task GerarRelatorioEstoqueExcelAsync(IEnumerable<ProdutoDto> produtos, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Estoque");

            ws.Cell(1, 1).Value = $"{_config.EmpresaNome} - Relatorio de Estoque";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 8).Merge();

            // Peso sai em QUILOS e como número (não como o texto "5,5 kg" do PDF): numa
            // planilha o valor existe para ser somado e filtrado — peso total da carga,
            // produtos acima de X kg —, e quilo é a unidade em que esse total é lido.
            var headers = new[] { "Codigo", "Nome", "Categoria", "Marca", "Estoque", "Unidade", "Peso (kg)", "Preco Venda" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(3, i + 1);
                cell.Value = headers[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            int row = 4;
            foreach (var p in produtos)
            {
                ws.Cell(row, 1).Value = p.CodigoInterno;
                ws.Cell(row, 2).Value = p.Nome;
                ws.Cell(row, 3).Value = p.CategoriaNome ?? "-";
                ws.Cell(row, 4).Value = p.MarcaNome ?? "-";
                ws.Cell(row, 5).Value = p.QuantidadeEstoque;
                ws.Cell(row, 6).Value = p.Unidade;
                if (PesoProdutoHelper.EmQuilos(p.PesoGramas) is { } pesoKg)
                {
                    ws.Cell(row, 7).Value = pesoKg;
                    ws.Cell(row, 7).Style.NumberFormat.Format = "#,##0.###";
                }
                ws.Cell(row, 8).Value = p.PrecoVenda;
                ws.Cell(row, 8).Style.NumberFormat.Format = "R$ #,##0.00";

                if (p.SemEstoque) ws.Cell(row, 5).Style.Font.FontColor = XLColor.Red;
                else if (p.EstoqueBaixo) ws.Cell(row, 5).Style.Font.FontColor = XLColor.Orange;

                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(caminhoArquivo);
        });
    }

    public Task GerarTabelaPrecosPdfAsync(
        IEnumerable<ProdutoDto> produtos,
        string titulo,
        string subtitulo,
        decimal acrescimoPercentual,
        string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            AdicionarCabecalhoRelatorio(document, titulo, subtitulo);

            var tabela = new ITextTable(new float[] { 2.2f, 4.5f, 2f }).UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "CODIGO DE BARRAS", "NOME DO PRODUTO", "PRECO DE VENDA ATUAL");

            var cultura = new System.Globalization.CultureInfo("pt-BR");
            var listaOrdenada = BinarySearchCollectionHelper.OrdenarPorId(produtos, p => p.Id);
            foreach (var produto in listaOrdenada.OrderBy(p => p.Nome))
            {
                var preco = TabelaPrecosHelper.CalcularPrecoExibicao(produto.PrecoVenda, acrescimoPercentual);
                tabela.AddCell(CelulaTabela(TabelaPrecosHelper.ObterCodigoBarrasExibicao(produto.CodigoBarras, produto.CodigoInterno)));
                tabela.AddCell(CelulaTabela(produto.NomeExibicao));
                tabela.AddCell(CelulaTabela(preco.ToString("C2", cultura), TextAlignment.RIGHT));
            }

            document.Add(tabela);
            document.Add(new ITextParagraph($"\nTotal de produtos: {produtos.Count()}")
                .SetFont(ObterFonte()).SetFontSize(11));
        });
    }

    public Task GerarTabelaPrecosExcelAsync(
        IEnumerable<ProdutoDto> produtos,
        string titulo,
        decimal acrescimoPercentual,
        string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Tabela de Precos");

            ws.Cell(1, 1).Value = $"{_config.EmpresaNome} - {titulo}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 3).Merge();

            ws.Cell(2, 1).Value = $"Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}";
            ws.Range(2, 1, 2, 3).Merge();

            var headers = new[] { "CODIGO DE BARRAS", "NOME DO PRODUTO", "PRECO DE VENDA ATUAL" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            int row = 5;
            var listaOrdenada = BinarySearchCollectionHelper.OrdenarPorId(produtos, p => p.Id);
            foreach (var produto in listaOrdenada.OrderBy(p => p.Nome))
            {
                var preco = TabelaPrecosHelper.CalcularPrecoExibicao(produto.PrecoVenda, acrescimoPercentual);
                ws.Cell(row, 1).Value = TabelaPrecosHelper.ObterCodigoBarrasExibicao(produto.CodigoBarras, produto.CodigoInterno);
                ws.Cell(row, 2).Value = produto.NomeExibicao;
                ws.Cell(row, 3).Value = preco;
                ws.Cell(row, 3).Style.NumberFormat.Format = "R$ #,##0.00";

                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(caminhoArquivo);
        });
    }

    public Task GerarRelatorioVendasConsolidadasPdfAsync(
        IEnumerable<LinhaRelatorioVendaConsolidadaDto> linhas, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            AdicionarCabecalhoRelatorio(document, "Relatorio Consolidado de Vendas (Geral)",
                $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy} — Balcao + Vendas Externas");

            var tabela = new ITextTable(new float[] { 1.5f, 1.1f, 1.4f, 2.2f, 0.7f, 1.1f, 1f, 1f, 1.1f, 1.4f }).UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "Data", "Origem", "Cod. Venda", "Cliente/Resumo", "Itens", "Subtotal", "Desconto", "Comissao", "Total", "Pagamento");

            var cultura = new System.Globalization.CultureInfo("pt-BR");
            var lista = linhas.ToList();
            foreach (var linha in lista)
            {
                tabela.AddCell(CelulaTabela(linha.DataVenda.ToString("dd/MM/yyyy HH:mm")));
                tabela.AddCell(CelulaTabela(linha.Origem));
                tabela.AddCell(CelulaTabela(linha.NumeroVenda));
                tabela.AddCell(CelulaTabela(linha.ClienteOuResumo));
                tabela.AddCell(CelulaTabela(linha.TotalItens.ToString(), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.Subtotal.ToString("C2", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.Desconto.ToString("C2", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.Comissao.ToString("C2", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.Total.ToString("C2", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.FormaPagamento ?? "—"));
            }

            document.Add(tabela);
            var totalGeral = lista.Sum(l => l.Total);
            var totalBalcao = lista.Where(l => l.Origem == "Balcão").Sum(l => l.Total);
            var totalExterna = lista.Where(l => l.Origem == "Externa").Sum(l => l.Total);
            var totalComissao = lista.Sum(l => l.Comissao);
            // Os totais sao LIQUIDOS (ja sem comissao); a comissao sai ao lado para nao
            // parecer que o numero de faturamento simplesmente encolheu sem explicacao.
            document.Add(new ITextParagraph(
                    $"\nTotal Geral: {totalGeral.ToString("C2", cultura)} | Balcao: {totalBalcao.ToString("C2", cultura)} | Externa: {totalExterna.ToString("C2", cultura)} | Comissoes: {totalComissao.ToString("C2", cultura)} | {lista.Count} venda(s)")
                .SetFont(ObterFonte(true)).SetFontSize(11).SetTextAlignment(TextAlignment.RIGHT));
        });
    }

    public Task GerarRelatorioVendasConsolidadasExcelAsync(
        IEnumerable<LinhaRelatorioVendaConsolidadaDto> linhas, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Vendas Consolidadas");

            ws.Cell(1, 1).Value = $"{_config.EmpresaNome} - Relatorio Consolidado de Vendas (Geral)";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 10).Merge();

            ws.Cell(2, 1).Value = $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy} — Balcao + Vendas Externas";
            ws.Range(2, 1, 2, 10).Merge();

            // "Total" e liquido (ja sem desconto e sem comissao) — a comissao tem coluna
            // propria para o numero ser explicavel, e nao um faturamento que encolheu sozinho.
            var headers = new[] { "Data", "Origem", "Cod. Venda", "Cliente/Resumo", "Itens", "Subtotal", "Desconto", "Comissao", "Total", "Pagamento" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            var row = 5;
            var lista = linhas.ToList();
            foreach (var linha in lista)
            {
                ws.Cell(row, 1).Value = linha.DataVenda.ToString("dd/MM/yyyy HH:mm");
                ws.Cell(row, 2).Value = linha.Origem;
                ws.Cell(row, 3).Value = linha.NumeroVenda;
                ws.Cell(row, 4).Value = linha.ClienteOuResumo;
                ws.Cell(row, 5).Value = linha.TotalItens;
                ws.Cell(row, 6).Value = linha.Subtotal;
                ws.Cell(row, 6).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 7).Value = linha.Desconto;
                ws.Cell(row, 7).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 8).Value = linha.Comissao;
                ws.Cell(row, 8).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 9).Value = linha.Total;
                ws.Cell(row, 9).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 10).Value = linha.FormaPagamento ?? "—";
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                row++;
            }

            var totalRow = row + 1;
            ws.Cell(totalRow, 7).Value = "Totais:";
            ws.Cell(totalRow, 7).Style.Font.Bold = true;
            ws.Cell(totalRow, 8).Value = lista.Sum(l => l.Comissao);
            ws.Cell(totalRow, 8).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Cell(totalRow, 8).Style.Font.Bold = true;
            ws.Cell(totalRow, 9).Value = lista.Sum(l => l.Total);
            ws.Cell(totalRow, 9).Style.NumberFormat.Format = "R$ #,##0.00";
            ws.Cell(totalRow, 9).Style.Font.Bold = true;

            ws.Columns().AdjustToContents();
            workbook.SaveAs(caminhoArquivo);
        });
    }

    public Task GerarRelatorioVendasExternasPdfAsync(
        IEnumerable<LinhaRelatorioVendaExternaDto> linhas, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            AdicionarCabecalhoRelatorio(document, "Relatorio de Vendas Externas",
                $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}");

            var tabela = new ITextTable(new float[] { 1.8f, 1.8f, 3f, 1.2f, 1.5f, 1.5f }).UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "Data Venda", "Cod. Venda", "Produto/Item", "Qtd", "Vlr Unit.", "Vlr Total");

            var cultura = new System.Globalization.CultureInfo("pt-BR");
            var lista = linhas.ToList();
            foreach (var linha in lista)
            {
                tabela.AddCell(CelulaTabela(linha.DataVenda.ToString("dd/MM/yyyy HH:mm")));
                tabela.AddCell(CelulaTabela(linha.CodigoVenda));
                tabela.AddCell(CelulaTabela(linha.ProdutoItem));
                tabela.AddCell(CelulaTabela(linha.QuantidadeVendida.ToString("G", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.ValorUnitario.ToString("C2", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.ValorTotal.ToString("C2", cultura), TextAlignment.RIGHT));
            }

            document.Add(tabela);
            var total = lista.Sum(l => l.ValorTotal);
            document.Add(new ITextParagraph($"\nTotal do Periodo: {total.ToString("C2", cultura)} | {lista.Count} linha(s)")
                .SetFont(ObterFonte(true)).SetFontSize(13).SetTextAlignment(TextAlignment.RIGHT));
        });
    }

    public Task GerarRelatorioVendasExternasExcelAsync(
        IEnumerable<LinhaRelatorioVendaExternaDto> linhas, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Vendas Externas");

            ws.Cell(1, 1).Value = $"{_config.EmpresaNome} - Relatorio de Vendas Externas";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 6).Merge();

            ws.Cell(2, 1).Value = $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}";
            ws.Range(2, 1, 2, 6).Merge();

            var headers = new[] { "Data Venda", "Cod. Venda", "Produto/Item", "Qtd Vendida", "Valor Unitario", "Valor Total" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            var row = 5;
            foreach (var linha in linhas)
            {
                ws.Cell(row, 1).Value = linha.DataVenda.ToString("dd/MM/yyyy HH:mm");
                ws.Cell(row, 2).Value = linha.CodigoVenda;
                ws.Cell(row, 3).Value = linha.ProdutoItem;
                ws.Cell(row, 4).Value = linha.QuantidadeVendida;
                ws.Cell(row, 5).Value = linha.ValorUnitario;
                ws.Cell(row, 5).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 6).Value = linha.ValorTotal;
                ws.Cell(row, 6).Style.NumberFormat.Format = "R$ #,##0.00";
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(caminhoArquivo);
        });
    }

    public Task GerarRelatorioRankingProdutosPdfAsync(
        IEnumerable<ProdutoRankingDto> ranking, string titulo, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            AdicionarCabecalhoRelatorio(document, titulo, $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy} — Balcao + Vendas Externas");

            var tabela = new ITextTable(new float[] { 0.8f, 1.5f, 3.5f, 1.5f, 2f }).UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "Pos.", "Codigo", "Nome do Produto", "Unidades", "Faturamento");

            var cultura = new System.Globalization.CultureInfo("pt-BR");
            foreach (var item in ranking)
            {
                tabela.AddCell(CelulaTabela(item.Posicao.ToString(), TextAlignment.CENTER));
                tabela.AddCell(CelulaTabela(item.CodigoInterno));
                tabela.AddCell(CelulaTabela(item.NomeProduto));
                tabela.AddCell(CelulaTabela(item.QuantidadeTotal.ToString("G", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(item.FaturamentoGerado.ToString("C2", cultura), TextAlignment.RIGHT));
            }

            document.Add(tabela);
            document.Add(new ITextParagraph($"\nProdutos listados: {ranking.Count()}")
                .SetFont(ObterFonte()).SetFontSize(11));
        });
    }

    public Task GerarRelatorioRankingProdutosExcelAsync(
        IEnumerable<ProdutoRankingDto> ranking, string titulo, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Ranking");

            ws.Cell(1, 1).Value = $"{_config.EmpresaNome} - {titulo}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 5).Merge();

            ws.Cell(2, 1).Value = $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}";
            ws.Range(2, 1, 2, 5).Merge();

            var headers = new[] { "Posicao", "Codigo", "Nome do Produto", "Total Unidades Vendidas", "Faturamento Gerado" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            var row = 5;
            foreach (var item in ranking)
            {
                ws.Cell(row, 1).Value = item.Posicao;
                ws.Cell(row, 2).Value = item.CodigoInterno;
                ws.Cell(row, 3).Value = item.NomeProduto;
                ws.Cell(row, 4).Value = item.QuantidadeTotal;
                ws.Cell(row, 5).Value = item.FaturamentoGerado;
                ws.Cell(row, 5).Style.NumberFormat.Format = "R$ #,##0.00";
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(caminhoArquivo);
        });
    }

    public Task GerarRelatorioProdutosEncalhadosPdfAsync(
        IEnumerable<ProdutoEncalhadoDto> produtos, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            AdicionarCabecalhoRelatorio(document, "Produtos Nunca Vendidos (Encalhados)",
                $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy} — Estoque com saldo e zero vendas");

            var tabela = new ITextTable(new float[] { 1.5f, 4f, 1.5f, 2f }).UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "Codigo", "Nome do Produto", "Estoque Atual", "Valor Parado");

            var cultura = new System.Globalization.CultureInfo("pt-BR");
            var lista = produtos.ToList();
            foreach (var item in lista)
            {
                tabela.AddCell(CelulaTabela(item.CodigoInterno));
                tabela.AddCell(CelulaTabela(item.NomeProduto));
                tabela.AddCell(CelulaTabela(item.EstoqueAtual.ToString("G", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(item.ValorTotalParado.ToString("C2", cultura), TextAlignment.RIGHT));
            }

            document.Add(tabela);
            var totalParado = lista.Sum(p => p.ValorTotalParado);
            document.Add(new ITextParagraph($"\nCapital parado estimado: {totalParado.ToString("C2", cultura)} | {lista.Count} produto(s)")
                .SetFont(ObterFonte(true)).SetFontSize(13).SetTextAlignment(TextAlignment.RIGHT));
        });
    }

    public Task GerarRelatorioProdutosEncalhadosExcelAsync(
        IEnumerable<ProdutoEncalhadoDto> produtos, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Encalhados");

            ws.Cell(1, 1).Value = $"{_config.EmpresaNome} - Produtos Nunca Vendidos";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 4).Merge();

            ws.Cell(2, 1).Value = $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}";
            ws.Range(2, 1, 2, 4).Merge();

            var headers = new[] { "Codigo", "Nome do Produto", "Estoque Atual", "Valor Total Parado" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            var row = 5;
            foreach (var item in produtos)
            {
                ws.Cell(row, 1).Value = item.CodigoInterno;
                ws.Cell(row, 2).Value = item.NomeProduto;
                ws.Cell(row, 3).Value = item.EstoqueAtual;
                ws.Cell(row, 4).Value = item.ValorTotalParado;
                ws.Cell(row, 4).Style.NumberFormat.Format = "R$ #,##0.00";
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(caminhoArquivo);
        });
    }

    public Task GerarRelatorioValidadePdfAsync(IEnumerable<ProdutoDto> produtos, int diasLimite, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            AdicionarCabecalhoRelatorio(document, "Produtos Próximos da Validade",
                $"Vencimento em até {diasLimite} dias (ou já vencidos) — Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}");

            var tabela = new ITextTable(new float[] { 1.5f, 3.5f, 1.5f, 1.5f, 1.5f }).UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "Codigo", "Nome", "Estoque", "Validade", "Situacao");

            var lista = produtos.ToList();
            foreach (var produto in lista)
            {
                tabela.AddCell(CelulaTabela(produto.CodigoInterno));
                tabela.AddCell(CelulaTabela(produto.NomeExibicao));
                tabela.AddCell(CelulaTabela($"{produto.QuantidadeEstoque:G} {produto.Unidade}", TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(produto.DataValidade?.ToString("dd/MM/yyyy") ?? "-", TextAlignment.CENTER));

                var celSituacao = CelulaTabela(DescreverSituacaoValidade(produto.DataValidade), TextAlignment.CENTER);
                var dias = DiasParaVencer(produto.DataValidade);
                if (dias is < 0) celSituacao.SetFontColor(new DeviceRgb(220, 53, 69));
                else if (dias is <= 5) celSituacao.SetFontColor(new DeviceRgb(253, 126, 20));
                tabela.AddCell(celSituacao);
            }

            document.Add(tabela);
            document.Add(new ITextParagraph($"\nTotal de produtos: {lista.Count}")
                .SetFont(ObterFonte()).SetFontSize(11));
        });
    }

    public Task GerarRelatorioValidadeExcelAsync(IEnumerable<ProdutoDto> produtos, int diasLimite, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Validade Proxima");

            ws.Cell(1, 1).Value = $"{_config.EmpresaNome} - Produtos Proximos da Validade";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 5).Merge();

            ws.Cell(2, 1).Value = $"Vencimento em ate {diasLimite} dias (ou ja vencidos) - Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}";
            ws.Range(2, 1, 2, 5).Merge();

            var headers = new[] { "Codigo", "Nome", "Estoque", "Validade", "Situacao" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            var row = 5;
            foreach (var produto in produtos)
            {
                ws.Cell(row, 1).Value = produto.CodigoInterno;
                ws.Cell(row, 2).Value = produto.NomeExibicao;
                ws.Cell(row, 3).Value = $"{produto.QuantidadeEstoque:G} {produto.Unidade}";
                ws.Cell(row, 4).Value = produto.DataValidade?.ToString("dd/MM/yyyy") ?? "-";
                ws.Cell(row, 5).Value = DescreverSituacaoValidade(produto.DataValidade);

                var dias = DiasParaVencer(produto.DataValidade);
                if (dias is < 0) ws.Cell(row, 5).Style.Font.FontColor = XLColor.Red;
                else if (dias is <= 5) ws.Cell(row, 5).Style.Font.FontColor = XLColor.Orange;

                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                row++;
            }

            ws.Columns().AdjustToContents();
            workbook.SaveAs(caminhoArquivo);
        });
    }

    public Task GerarOrcamentoPdfAsync(OrcamentoDto orcamento, string caminhoArquivo)
    {
        ArgumentNullException.ThrowIfNull(orcamento);

        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4);
            document.SetMargins(30, 30, 30, 30);

            var cultura = new System.Globalization.CultureInfo("pt-BR");

            AdicionarCabecalhoOrcamento(document, orcamento);

            var tabela = new ITextTable(new float[] { 1.6f, 4.4f, 1f, 1f, 1.6f, 1.8f }).UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "Codigo", "Produto/Servico", "Qtd", "Un", "Vlr Unit.", "Subtotal");

            foreach (var item in orcamento.Itens)
            {
                tabela.AddCell(CelulaTabela(string.IsNullOrWhiteSpace(item.CodigoProduto) ? "-" : item.CodigoProduto));
                tabela.AddCell(CelulaTabela(item.NomeProduto));
                tabela.AddCell(CelulaTabela(FormatarQuantidadeCupom(item.Quantidade), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(string.IsNullOrWhiteSpace(item.Unidade) ? "UN" : item.Unidade, TextAlignment.CENTER));
                tabela.AddCell(CelulaTabela(item.PrecoUnitario.ToString("C2", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(item.Subtotal.ToString("C2", cultura), TextAlignment.RIGHT));
            }

            document.Add(tabela);

            document.Add(new ITextParagraph($"Subtotal: {orcamento.Subtotal.ToString("C2", cultura)}")
                .SetFont(ObterFonte()).SetFontSize(11).SetTextAlignment(TextAlignment.RIGHT).SetMarginTop(10));

            if (orcamento.Desconto > 0)
            {
                document.Add(new ITextParagraph($"Desconto: -{orcamento.Desconto.ToString("C2", cultura)}")
                    .SetFont(ObterFonte()).SetFontSize(11).SetTextAlignment(TextAlignment.RIGHT));
            }

            document.Add(new ITextParagraph($"TOTAL: {orcamento.Total.ToString("C2", cultura)}")
                .SetFont(ObterFonte(true)).SetFontSize(15).SetTextAlignment(TextAlignment.RIGHT).SetMarginTop(4));

            if (!string.IsNullOrWhiteSpace(orcamento.Observacoes))
            {
                document.Add(new ITextParagraph("Observacoes")
                    .SetFont(ObterFonte(true)).SetFontSize(11).SetMarginTop(18));
                document.Add(new ITextParagraph(orcamento.Observacoes)
                    .SetFont(ObterFonte()).SetFontSize(10).SetMarginTop(2));
            }

            // Orçamento não gera venda, não reserva estoque e não movimenta financeiro:
            // o aviso abaixo deixa isso explícito para o cliente que recebe o PDF.
            document.Add(new ITextParagraph(
                    $"Este documento e apenas uma proposta comercial, sem valor fiscal e sem compromisso de venda. " +
                    $"Precos e condicoes validos ate {orcamento.DataValidade:dd/MM/yyyy}, sujeitos a disponibilidade de estoque.")
                .SetFont(ObterFonte()).SetFontSize(9)
                .SetFontColor(new DeviceRgb(108, 117, 125))
                .SetTextAlignment(TextAlignment.CENTER).SetMarginTop(25));

            document.Add(new ITextParagraph(_config.CupomRodape)
                .SetFont(ObterFonte(true)).SetFontSize(10)
                .SetTextAlignment(TextAlignment.CENTER).SetMarginTop(8));
        });
    }

    /// <summary>
    /// Cabecalho do orcamento: logo e dados da empresa, sem titulo de relatorio.
    /// O orcamento vai para a mao do cliente, entao o topo funciona como papel timbrado —
    /// o numero do documento fica no bloco de dados logo abaixo, nao no titulo.
    /// </summary>
    private void AdicionarCabecalhoOrcamento(Document document, OrcamentoDto orcamento)
    {
        var logo = CarregarLogoPdf();

        if (logo is not null)
        {
            var topo = new ITextTable(new float[] { 1f, 3.4f }).UseAllAvailableWidth();
            topo.AddCell(new ITextCell().Add(logo)
                .SetBorder(iText.Layout.Borders.Border.NO_BORDER)
                .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                .SetPadding(0));
            topo.AddCell(new ITextCell().Add(MontarBlocoEmpresa(TextAlignment.LEFT))
                .SetBorder(iText.Layout.Borders.Border.NO_BORDER)
                .SetVerticalAlignment(VerticalAlignment.MIDDLE)
                .SetPaddingLeft(12));
            document.Add((IBlockElement)topo);
        }
        else
        {
            document.Add(MontarBlocoEmpresa(TextAlignment.CENTER));
        }

        var separador = new ITextTable(1).UseAllAvailableWidth().SetMarginTop(12).SetMarginBottom(14);
        separador.AddCell(new ITextCell().SetHeight(2).SetBackgroundColor(new DeviceRgb(245, 194, 0))
            .SetBorder(iText.Layout.Borders.Border.NO_BORDER));
        document.Add((IBlockElement)separador);

        var dados = new ITextTable(new float[] { 1f, 1f }).UseAllAvailableWidth().SetMarginBottom(14);
        dados.AddCell(CelulaTabela($"Orcamento: {orcamento.NumeroOrcamento}"));
        dados.AddCell(CelulaTabela($"Emissao: {orcamento.DataOrcamento:dd/MM/yyyy}"));
        dados.AddCell(CelulaTabela($"Cliente: {orcamento.NomeCliente}"));
        dados.AddCell(CelulaTabela(
            string.IsNullOrWhiteSpace(orcamento.TelefoneCliente) ? "Telefone: -" : $"Telefone: {orcamento.TelefoneCliente}"));
        dados.AddCell(CelulaTabela($"Validade: {orcamento.DataValidade:dd/MM/yyyy}"));
        dados.AddCell(CelulaTabela($"Situacao: {orcamento.StatusDescricao}"));
        dados.AddCell(CelulaTabela(
            string.IsNullOrWhiteSpace(orcamento.Usuario) ? "Atendente: -" : $"Atendente: {orcamento.Usuario}"));
        dados.AddCell(CelulaTabela(string.Empty));
        document.Add((IBlockElement)dados);
    }

    /// <summary>Nome, razao social e todos os contatos preenchidos no cadastro da empresa.</summary>
    private IBlockElement MontarBlocoEmpresa(TextAlignment alinhamento)
    {
        var bloco = new Div();

        bloco.Add(new ITextParagraph(Empresa.NomeFantasia.ToUpperInvariant())
            .SetFont(ObterFonte(true)).SetFontSize(18)
            .SetFontColor(ColorConstants.BLACK).SetTextAlignment(alinhamento).SetMargin(0));

        foreach (var linha in MontarLinhasEmpresa())
        {
            bloco.Add(new ITextParagraph(linha)
                .SetFont(ObterFonte()).SetFontSize(9)
                .SetFontColor(ColorConstants.BLACK).SetTextAlignment(alinhamento)
                .SetMargin(0).SetMarginTop(2));
        }

        return bloco;
    }

    private IEnumerable<string> MontarLinhasEmpresa()
    {
        if (!string.IsNullOrWhiteSpace(Empresa.RazaoSocial) &&
            !string.Equals(Empresa.RazaoSocial, Empresa.NomeFantasia, StringComparison.OrdinalIgnoreCase))
        {
            yield return Empresa.RazaoSocial;
        }

        if (!string.IsNullOrWhiteSpace(Empresa.Subtitulo))
            yield return Empresa.Subtitulo;

        var documentos = new List<string>();
        if (!string.IsNullOrWhiteSpace(Empresa.CNPJ)) documentos.Add($"CNPJ: {Empresa.CNPJ}");
        if (!string.IsNullOrWhiteSpace(Empresa.InscricaoEstadual)) documentos.Add($"IE: {Empresa.InscricaoEstadual}");
        if (documentos.Count > 0) yield return string.Join("   |   ", documentos);

        if (!string.IsNullOrWhiteSpace(Empresa.Endereco))
            yield return Empresa.Endereco;

        var contatos = new List<string>();
        if (!string.IsNullOrWhiteSpace(Empresa.Telefone)) contatos.Add($"Tel: {Empresa.Telefone}");
        if (!string.IsNullOrWhiteSpace(Empresa.Email)) contatos.Add(Empresa.Email);
        if (contatos.Count > 0) yield return string.Join("   |   ", contatos);
    }

    /// <summary>
    /// Logo do cabecalho. Devolve null quando o arquivo nao existe ou nao e uma imagem
    /// valida: o orcamento ainda precisa sair, so que sem a logo.
    /// </summary>
    private Image? CarregarLogoPdf()
    {
        var caminho = _config.LogoSemFundoPath;
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
            caminho = _config.LogoPath;

        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
            return null;

        try
        {
            return new Image(iText.IO.Image.ImageDataFactory.Create(caminho))
                .ScaleToFit(90, 90)
                .SetHorizontalAlignment(HorizontalAlignment.LEFT);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int? DiasParaVencer(DateTime? dataValidade)
        => dataValidade.HasValue ? (dataValidade.Value.Date - DateTime.Today).Days : null;

    private static string DescreverSituacaoValidade(DateTime? dataValidade)
    {
        var dias = DiasParaVencer(dataValidade);
        return dias switch
        {
            null => "-",
            < 0 => $"Vencido há {-dias.Value} dia(s)",
            0 => "Vence hoje",
            1 => "Vence amanhã",
            _ => $"Vence em {dias} dias"
        };
    }

    // ---------- Vendas por Canal e Produto ----------

    public Task GerarRelatorioVendasPorCanalPdfAsync(
        IEnumerable<LinhaVendaPorCanalDto> linhas, IEnumerable<TotalCanalDto> totais,
        DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            // Paisagem: sao oito colunas, e em retrato o nome do produto fica espremido a
            // ponto de o relatorio nao servir para conferencia.
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4.Rotate());
            document.SetMargins(30, 30, 30, 30);

            AdicionarCabecalhoRelatorio(document, "Vendas por Canal e Produto",
                $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}");

            var tabela = new ITextTable(new float[] { 1.6f, 1.6f, 1.3f, 3.2f, 1.6f, 0.9f, 1.2f, 1.3f })
                .UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "Data Venda", "Canal", "Cod. Produto", "Produto",
                "Nr. Venda", "Qtd", "Vlr Unit.", "Vlr Total");

            var cultura = new System.Globalization.CultureInfo("pt-BR");
            var lista = linhas.ToList();
            foreach (var linha in lista)
            {
                tabela.AddCell(CelulaTabela(linha.DataVenda.ToString("dd/MM/yyyy HH:mm")));
                tabela.AddCell(CelulaTabela(linha.CanalDescricao));
                tabela.AddCell(CelulaTabela(linha.CodigoExibicao));
                tabela.AddCell(CelulaTabela(linha.NomeProduto));
                tabela.AddCell(CelulaTabela(linha.NumeroVenda));
                tabela.AddCell(CelulaTabela(linha.Quantidade.ToString("G", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.ValorUnitario.ToString("C2", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.ValorTotal.ToString("C2", cultura), TextAlignment.RIGHT));
            }

            document.Add(tabela);
            AdicionarLinhaSeparadora(document);

            document.Add(new ITextParagraph("Totais por canal").SetFont(ObterFonte(true)).SetFontSize(12)
                .SetMarginTop(10));

            var tabelaTotais = new ITextTable(new float[] { 3f, 1.2f, 1.2f, 1.6f }).UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabelaTotais, "Canal", "Linhas", "Itens", "Faturado");
            foreach (var total in totais)
            {
                tabelaTotais.AddCell(CelulaTabela(total.CanalDescricao));
                tabelaTotais.AddCell(CelulaTabela(total.QuantidadeLinhas.ToString(cultura), TextAlignment.RIGHT));
                tabelaTotais.AddCell(CelulaTabela(total.QuantidadeItens.ToString("G", cultura), TextAlignment.RIGHT));
                tabelaTotais.AddCell(CelulaTabela(total.ValorTotal.ToString("C2", cultura), TextAlignment.RIGHT));
            }
            document.Add(tabelaTotais);

            var geral = lista.Sum(l => l.ValorTotal);
            document.Add(new ITextParagraph($"\nTotal do Periodo: {geral.ToString("C2", cultura)} | {lista.Count} linha(s)")
                .SetFont(ObterFonte(true)).SetFontSize(13).SetTextAlignment(TextAlignment.RIGHT));
        });
    }

    public Task GerarRelatorioVendasPorCanalExcelAsync(
        IEnumerable<LinhaVendaPorCanalDto> linhas, IEnumerable<TotalCanalDto> totais,
        DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Vendas por Canal");

            ws.Cell(1, 1).Value = $"{_config.EmpresaNome} - Vendas por Canal e Produto";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 8).Merge();

            ws.Cell(2, 1).Value = $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}";
            ws.Range(2, 1, 2, 8).Merge();

            var headers = new[] { "Data Venda", "Canal", "Cod. Produto", "Produto", "Nr. Venda",
                                  "Quantidade", "Valor Unitario", "Valor Total" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            var row = 5;
            foreach (var linha in linhas)
            {
                // Data e valores vao como numero, nao como texto: e o que permite ao lojista
                // filtrar por canal e somar no proprio Excel, que e o uso real deste arquivo.
                ws.Cell(row, 1).Value = linha.DataVenda;
                ws.Cell(row, 1).Style.NumberFormat.Format = "dd/mm/yyyy hh:mm";
                ws.Cell(row, 2).Value = linha.CanalDescricao;
                ws.Cell(row, 3).Value = linha.CodigoExibicao;
                ws.Cell(row, 4).Value = linha.NomeProduto;
                ws.Cell(row, 5).Value = linha.NumeroVenda;
                ws.Cell(row, 6).Value = linha.Quantidade;
                ws.Cell(row, 7).Value = linha.ValorUnitario;
                ws.Cell(row, 7).Style.NumberFormat.Format = "R$ #,##0.00";
                ws.Cell(row, 8).Value = linha.ValorTotal;
                ws.Cell(row, 8).Style.NumberFormat.Format = "R$ #,##0.00";
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                row++;
            }

            if (row > 5)
                ws.Range(4, 1, row - 1, headers.Length).SetAutoFilter();

            ws.Columns().AdjustToContents();

            var wsTotais = workbook.AddWorksheet("Totais por Canal");
            var headersTotais = new[] { "Canal", "Linhas", "Itens", "Faturado" };
            for (var i = 0; i < headersTotais.Length; i++)
            {
                var cell = wsTotais.Cell(1, i + 1);
                cell.Value = headersTotais[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            var linhaTotal = 2;
            foreach (var total in totais)
            {
                wsTotais.Cell(linhaTotal, 1).Value = total.CanalDescricao;
                wsTotais.Cell(linhaTotal, 2).Value = total.QuantidadeLinhas;
                wsTotais.Cell(linhaTotal, 3).Value = total.QuantidadeItens;
                wsTotais.Cell(linhaTotal, 4).Value = total.ValorTotal;
                wsTotais.Cell(linhaTotal, 4).Style.NumberFormat.Format = "R$ #,##0.00";
                linhaTotal++;
            }
            wsTotais.Columns().AdjustToContents();

            workbook.SaveAs(caminhoArquivo);
        });
    }

    // ---------- Movimentacao de Produtos ----------

    public Task GerarRelatorioMovimentacaoProdutosPdfAsync(
        IEnumerable<LinhaMovimentacaoProdutoDto> linhas, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var writer = new PdfWriter(caminhoArquivo);
            using var pdf = new PdfDocument(writer);
            using var document = new Document(pdf, iText.Kernel.Geom.PageSize.A4.Rotate());
            document.SetMargins(30, 30, 30, 30);

            AdicionarCabecalhoRelatorio(document, "Movimentacao de Produtos (Entrada/Saida)",
                $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}");

            var tabela = new ITextTable(new float[] { 1.5f, 1.2f, 2.8f, 1f, 0.9f, 1f, 1f, 2.6f })
                .UseAllAvailableWidth();
            AdicionarCabecalhoTabela(tabela, "Data", "Cod. Produto", "Produto", "Tipo",
                "Qtd", "Saldo Ant.", "Saldo Pos.", "Origem");

            var cultura = new System.Globalization.CultureInfo("pt-BR");
            var lista = linhas.ToList();
            foreach (var linha in lista)
            {
                tabela.AddCell(CelulaTabela(linha.Data.ToString("dd/MM/yyyy HH:mm")));
                tabela.AddCell(CelulaTabela(linha.CodigoProduto));
                tabela.AddCell(CelulaTabela(linha.NomeProduto));
                tabela.AddCell(CelulaTabela(linha.TipoDescricao));
                tabela.AddCell(CelulaTabela(linha.Quantidade.ToString("G", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.SaldoAnterior.ToString("G", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.SaldoPosterior.ToString("G", cultura), TextAlignment.RIGHT));
                tabela.AddCell(CelulaTabela(linha.OrigemDescricao));
            }

            document.Add(tabela);
            AdicionarLinhaSeparadora(document);

            var entradas = lista.Where(l => l.Tipo == TipoMovimentacao.Entrada).Sum(l => l.Quantidade);
            var saidas = lista.Where(l => l.Tipo == TipoMovimentacao.Saida).Sum(l => l.Quantidade);
            var ajustes = lista.Count(l => l.Tipo == TipoMovimentacao.Ajuste);

            document.Add(new ITextParagraph(
                    $"\nEntradas: {entradas.ToString("G", cultura)} | Saidas: {saidas.ToString("G", cultura)} | " +
                    $"Ajustes: {ajustes} | {lista.Count} movimentacao(oes)")
                .SetFont(ObterFonte(true)).SetFontSize(12).SetTextAlignment(TextAlignment.RIGHT));
        });
    }

    public Task GerarRelatorioMovimentacaoProdutosExcelAsync(
        IEnumerable<LinhaMovimentacaoProdutoDto> linhas, DateTime inicio, DateTime fim, string caminhoArquivo)
    {
        return Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.AddWorksheet("Movimentacoes");

            ws.Cell(1, 1).Value = $"{_config.EmpresaNome} - Movimentacao de Produtos";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 10).Merge();

            ws.Cell(2, 1).Value = $"Periodo: {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}";
            ws.Range(2, 1, 2, 10).Merge();

            var headers = new[] { "Data", "Cod. Produto", "Produto", "Unidade", "Tipo",
                                  "Quantidade", "Qtd com Sinal", "Saldo Anterior", "Saldo Posterior", "Origem" };
            for (var i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(4, i + 1);
                cell.Value = headers[i];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(245, 194, 0);
                cell.Style.Font.Bold = true;
            }

            var row = 5;
            foreach (var linha in linhas)
            {
                ws.Cell(row, 1).Value = linha.Data;
                ws.Cell(row, 1).Style.NumberFormat.Format = "dd/mm/yyyy hh:mm";
                ws.Cell(row, 2).Value = linha.CodigoProduto;
                ws.Cell(row, 3).Value = linha.NomeProduto;
                ws.Cell(row, 4).Value = linha.Unidade;
                ws.Cell(row, 5).Value = linha.TipoDescricao;
                ws.Cell(row, 6).Value = linha.Quantidade;
                // Coluna com sinal: somada, da o saldo movimentado no periodo sem o lojista
                // precisar separar entradas de saidas a mao.
                ws.Cell(row, 7).Value = linha.QuantidadeComSinal;
                ws.Cell(row, 8).Value = linha.SaldoAnterior;
                ws.Cell(row, 9).Value = linha.SaldoPosterior;
                ws.Cell(row, 10).Value = linha.OrigemDescricao;
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(248, 249, 250);
                row++;
            }

            if (row > 5)
                ws.Range(4, 1, row - 1, headers.Length).SetAutoFilter();

            ws.Columns().AdjustToContents();
            workbook.SaveAs(caminhoArquivo);
        });
    }

    private void AdicionarCabecalhoRelatorio(Document document, string titulo, string subtitulo)
    {
        document.Add(new ITextParagraph(_config.EmpresaNome.ToUpperInvariant()).SetFont(ObterFonte(true)).SetFontSize(18)
            .SetFontColor(ColorConstants.BLACK).SetTextAlignment(TextAlignment.CENTER));
        document.Add(new ITextParagraph(_config.EmpresaSubtitulo).SetFont(ObterFonte()).SetFontSize(11)
            .SetFontColor(ColorConstants.BLACK).SetTextAlignment(TextAlignment.CENTER));
        document.Add(new ITextParagraph(titulo).SetFont(ObterFonte(true)).SetFontSize(14)
            .SetTextAlignment(TextAlignment.CENTER).SetMarginTop(10));
        document.Add(new ITextParagraph(subtitulo).SetFont(ObterFonte()).SetFontSize(10)
            .SetFontColor(ColorConstants.BLACK).SetTextAlignment(TextAlignment.CENTER).SetMarginBottom(15));

        var separador = new ITextTable(1).UseAllAvailableWidth();
        separador.AddCell(new ITextCell().SetHeight(2).SetBackgroundColor(new DeviceRgb(245, 194, 0))
            .SetBorder(iText.Layout.Borders.Border.NO_BORDER));
        document.Add((IBlockElement)separador.SetMarginBottom(15));
    }

    private static void AdicionarCabecalhoTabela(ITextTable tabela, params string[] headers)
    {
        foreach (var header in headers)
        {
            tabela.AddHeaderCell(new ITextCell().Add(new ITextParagraph(header).SetFont(ObterFonte(true)).SetFontSize(9))
                .SetBackgroundColor(new DeviceRgb(245, 194, 0))
                .SetBorder(iText.Layout.Borders.Border.NO_BORDER)
                .SetPadding(5));
        }
    }

    private static ITextCell CelulaTabela(string texto, TextAlignment alignment = TextAlignment.LEFT)
        => new ITextCell().Add(new ITextParagraph(texto).SetFont(ObterFonte()).SetFontSize(9).SetTextAlignment(alignment))
            .SetBorder(iText.Layout.Borders.Border.NO_BORDER)
            .SetBorderBottom(new iText.Layout.Borders.SolidBorder(new DeviceRgb(233, 236, 239), 0.5f))
            .SetPadding(4);

    private static void AdicionarLinhaSeparadora(Document document)
    {
        var tabela = new ITextTable(1).UseAllAvailableWidth().SetMarginTop(3).SetMarginBottom(3);
        tabela.AddCell(new ITextCell().SetHeight(0.5f).SetBackgroundColor(new DeviceRgb(233, 236, 239))
            .SetBorder(iText.Layout.Borders.Border.NO_BORDER));
        document.Add((IBlockElement)tabela);
    }
}
