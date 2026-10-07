using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Helpers;
using ImperialColors.UI.Helpers;
using ImperialColors.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ImperialColors.UI.Views;

public partial class RelatoriosView : UserControl
{
    private readonly IServiceProvider _serviceProvider;
    private Button? _navAtivo;
    private string _tipoRelatorio = "VendasPeriodo";

    private static readonly Dictionary<string, (string Titulo, string Descricao, string Colunas, bool PrecisaPeriodo, bool PrecisaGiro)> Metadados = new()
    {
        ["VendasPeriodo"] = (
            "Vendas por Período",
            "Consolidado de vendas finalizadas no balcão dentro do intervalo selecionado.",
            "Data, código da venda, cliente, forma de pagamento, itens e totais.",
            true, false),
        ["VendasExternas"] = (
            "Relatório de Vendas Externas",
            "Auditoria detalhada por item das vendas de rua registradas no período.",
            "Data da venda, código, produto/item, quantidade, valor unitário e valor total.",
            true, false),
        ["VendasConsolidadas"] = (
            "Relatório Consolidado de Vendas (Geral)",
            "Unifica vendas de balcão (PDV) e vendas externas em uma única listagem filtrada por período.",
            "Data, origem (Balcão ou Externa), código da venda, cliente/resumo, itens, subtotal, desconto, total e forma de pagamento.",
            true, false),
        ["VendasPorCanal"] = (
            "Vendas por Canal e Produto",
            "Cada produto vendido no período com o canal por onde a venda entrou — loja física (PDV), venda externa na rua e site. Traz os totais de cada canal no rodapé.",
            "Data da venda, canal, código do produto, produto, número da venda, quantidade, valor unitário e valor total.",
            true, false),
        ["MovimentacaoProdutos"] = (
            "Movimentação de Produtos (Entrada/Saída)",
            "Extrato de entradas e saídas de estoque por produto e data: quando o item entrou e em que dias saiu, com o documento que originou cada movimentação.",
            "Data, código do produto, produto, unidade, tipo (entrada/saída/ajuste), quantidade, saldo anterior, saldo posterior e origem.",
            true, false),
        ["EstoqueCompleto"] = (
            "Estoque Completo",
            "Inventário de todos os produtos ativos com saldo, categoria e preço de venda.",
            "Código, nome, categoria, unidade, estoque, estoque mínimo e preço.",
            false, false),
        ["EstoqueBaixo"] = (
            "Estoque Baixo",
            "Produtos ativos com quantidade abaixo do estoque mínimo configurado.",
            "Código, nome, estoque atual, estoque mínimo e diferença.",
            false, false),
        ["SemEstoque"] = (
            "Sem Estoque",
            "Produtos ativos com quantidade zerada — itens que precisam de reposição.",
            "Código, nome, categoria e preço de venda.",
            false, false),
        ["ValidadeProxima"] = (
            "Próximos da Validade",
            "Produtos com saldo em estoque cuja validade vence em até 15 dias (inclui os já vencidos).",
            "Código, nome, estoque atual, data de validade e situação (dias para vencer).",
            false, false),
        ["TabelaLoja"] = (
            "Tabela de Vendas da Loja",
            "Catálogo de preços de balcão para consulta ou impressão.",
            "Código de barras, nome do produto e preço atual de venda.",
            false, false),
        ["TabelaPintor"] = (
            "Tabela do Pintor",
            "Mesma estrutura da tabela da loja, com acréscimo automático de 5% no preço exibido.",
            "Código de barras, nome do produto e preço com acréscimo para parceiros.",
            false, false),
        ["AnaliseGiro"] = (
            "Análise de Giro e Desempenho",
            "Ranking cruzando vendas de balcão e externas. Identifique campeões de venda, itens parados ou encalhados.",
            "Mais/Menos vendidos: posição, código, produto, unidades e faturamento. Encalhados: código, produto, estoque e valor parado.",
            true, true)
    };

    public RelatoriosView(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;

        var inicioPadrao = DateTime.Today.AddDays(-30);
        DpInicio.SelectedDate = inicioPadrao;
        DpFim.SelectedDate = DateTime.Today;

        Loaded += (_, _) =>
        {
            DatePickerSyncHelper.SincronizarTexto(DpInicio);
            DatePickerSyncHelper.SincronizarTexto(DpFim);
            SelecionarRelatorio(BtnNavVendasPeriodo, "VendasPeriodo");
        };
    }

    private void BtnNavRelatorio_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button botao || botao.Tag is not string tipo)
            return;

        SelecionarRelatorio(botao, tipo);
    }

    private void SelecionarRelatorio(Button botao, string tipo)
    {
        if (_navAtivo != null)
            NavMenuHelper.SetIsActive(_navAtivo, false);

        _navAtivo = botao;
        _tipoRelatorio = tipo;
        NavMenuHelper.SetIsActive(botao, true);
        AtualizarPainelDetalhe();
    }

    private void AtualizarPainelDetalhe()
    {
        if (!Metadados.TryGetValue(_tipoRelatorio, out var meta))
            return;

        TxtTituloRelatorio.Text = meta.Titulo;
        TxtDescricaoRelatorio.Text = meta.Descricao;
        TxtColunasRelatorio.Text = meta.Colunas;
        PainelPeriodo.Visibility = meta.PrecisaPeriodo ? Visibility.Visible : Visibility.Collapsed;
        PainelGiro.Visibility = meta.PrecisaGiro ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnFormato_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicado)
            return;

        BtnFormatoPdf.IsChecked = clicado == BtnFormatoPdf;
        BtnFormatoExcel.IsChecked = clicado == BtnFormatoExcel;
    }

    private bool ExportarExcel => BtnFormatoExcel.IsChecked == true;

    private static (DateTime inicio, DateTime fim) ObterPeriodo(DatePicker dpInicio, DatePicker dpFim)
    {
        var inicio = dpInicio.SelectedDate ?? DateTime.Today.AddDays(-30);
        var fim = dpFim.SelectedDate ?? DateTime.Today;
        return (inicio, fim.AddDays(1).AddSeconds(-1));
    }

    private (DateTime inicio, DateTime fim) ObterPeriodo()
        => ObterPeriodo(DpInicio, DpFim);

    private static bool TentarObterCaminhoSalvar(string nomePadrao, bool excel, out string caminho)
    {
        var saveDialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = nomePadrao,
            DefaultExt = excel ? ".xlsx" : ".pdf",
            Filter = excel ? "Excel|*.xlsx" : "PDF|*.pdf"
        };

        if (saveDialog.ShowDialog() == true)
        {
            caminho = saveDialog.FileName;
            return true;
        }

        caminho = string.Empty;
        return false;
    }

    private static void NotificarSucesso(string caminho)
        => MessageBox.Show($"Arquivo gerado com sucesso:\n{caminho}", "Relatório", MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>
    /// Notificação dos relatórios de controle, que além do arquivo escolhido pelo operador
    /// guardam uma cópia no arquivo por data. A mensagem diz onde a cópia ficou — senão o
    /// histórico existiria sem ninguém saber que existe.
    ///
    /// Quando o arquivamento falha (pasta sem permissão, disco cheio), o relatório pedido
    /// já está salvo: a tela avisa que só a cópia não foi guardada, em vez de dar o
    /// trabalho inteiro por perdido.
    /// </summary>
    private static void NotificarSucessoComArquivo(string caminho, string? caminhoArquivado)
    {
        if (caminhoArquivado is null)
        {
            MessageBox.Show(
                $"Arquivo gerado com sucesso:\n{caminho}\n\n" +
                "Obs.: não foi possível guardar a cópia na pasta de histórico " +
                $"({RelatorioArquivoHelper.ObterDiretorioRaiz()}). Verifique a permissão da pasta.",
                "Relatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(
            $"Arquivo gerado com sucesso:\n{caminho}\n\nCópia arquivada em:\n{caminhoArquivado}",
            "Relatório", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Vendas do período por produto, separadas pelo canal por onde entraram. Venda de
    /// balcão é sempre loja física; venda externa carrega o canal escolhido no cadastro.
    /// </summary>
    private async Task GerarVendasPorCanalAsync()
    {
        var (inicio, fim) = ObterPeriodo();
        var excel = ExportarExcel;
        if (!TentarObterCaminhoSalvar($"VendasPorCanal_{inicio:yyyyMMdd}_{fim:yyyyMMdd}", excel, out var caminho))
            return;

        var analytics = _serviceProvider.GetRequiredService<IRelatorioAnalyticsService>();
        var linhas = await analytics.ObterVendasPorCanalAsync(inicio, fim);
        var totais = analytics.TotalizarPorCanal(linhas);
        var relatorio = _serviceProvider.GetRequiredService<IRelatorioService>();

        if (excel)
            await relatorio.GerarRelatorioVendasPorCanalExcelAsync(linhas, totais, inicio, fim, caminho);
        else
            await relatorio.GerarRelatorioVendasPorCanalPdfAsync(linhas, totais, inicio, fim, caminho);

        NotificarSucessoComArquivo(caminho, RelatorioArquivoHelper.ArquivarCopia(caminho, Relogio.Agora));
    }

    /// <summary>
    /// Extrato de entradas e saídas do período, por produto e data — quando o item entrou e
    /// em que dias saiu.
    /// </summary>
    private async Task GerarMovimentacaoProdutosAsync()
    {
        var (inicio, fim) = ObterPeriodo();
        var excel = ExportarExcel;
        if (!TentarObterCaminhoSalvar($"MovimentacaoProdutos_{inicio:yyyyMMdd}_{fim:yyyyMMdd}", excel, out var caminho))
            return;

        var linhas = await _serviceProvider.GetRequiredService<IRelatorioAnalyticsService>()
            .ObterMovimentacoesProdutosAsync(inicio, fim);
        var relatorio = _serviceProvider.GetRequiredService<IRelatorioService>();

        if (excel)
            await relatorio.GerarRelatorioMovimentacaoProdutosExcelAsync(linhas, inicio, fim, caminho);
        else
            await relatorio.GerarRelatorioMovimentacaoProdutosPdfAsync(linhas, inicio, fim, caminho);

        NotificarSucessoComArquivo(caminho, RelatorioArquivoHelper.ArquivarCopia(caminho, Relogio.Agora));
    }

    private static void NotificarErro(Exception ex)
        => MessageBox.Show($"Erro ao gerar relatório: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);

    private async void BtnGerar_Click(object sender, RoutedEventArgs e)
    {
        BtnGerar.IsEnabled = false;
        try
        {
            switch (_tipoRelatorio)
            {
                case "VendasPeriodo": await GerarVendasPeriodoAsync(); break;
                case "VendasExternas": await GerarVendasExternasAsync(); break;
                case "VendasConsolidadas": await GerarVendasConsolidadasAsync(); break;
                case "VendasPorCanal": await GerarVendasPorCanalAsync(); break;
                case "MovimentacaoProdutos": await GerarMovimentacaoProdutosAsync(); break;
                case "EstoqueCompleto": await GerarEstoqueAsync(p => p.ObterTodosAsync()); break;
                case "EstoqueBaixo": await GerarEstoqueAsync(p => p.ObterComEstoqueBaixoAsync()); break;
                case "SemEstoque": await GerarEstoqueAsync(p => p.ObterSemEstoqueAsync()); break;
                case "ValidadeProxima": await GerarValidadeProximaAsync(); break;
                case "TabelaLoja": await GerarTabelaPrecosAsync(0m, "TabelaVendasLoja", "Tabela de Vendas da Loja", "Catalogo de precos de balcao"); break;
                case "TabelaPintor": await GerarTabelaPrecosAsync(TabelaPrecosHelper.AcrescimoTabelaPintorPercentual, "TabelaPintor", "Tabela do Pintor", $"Precos com acrescimo de {TabelaPrecosHelper.AcrescimoTabelaPintorPercentual:N0}%"); break;
                case "AnaliseGiro": await GerarAnaliseGiroAsync(); break;
            }
        }
        catch (Exception ex) { NotificarErro(ex); }
        finally { BtnGerar.IsEnabled = true; }
    }

    private async Task GerarVendasPeriodoAsync()
    {
        var (inicio, fim) = ObterPeriodo();
        var excel = ExportarExcel;
        if (!TentarObterCaminhoSalvar($"Vendas_{inicio:yyyyMMdd}_{fim:yyyyMMdd}", excel, out var caminho))
            return;

        var vendas = await _serviceProvider.GetRequiredService<IVendaService>().ObterPorPeriodoAsync(inicio, fim);
        var relatorio = _serviceProvider.GetRequiredService<IRelatorioService>();

        if (excel)
            await relatorio.GerarRelatorioVendasExcelAsync(vendas, inicio, fim, caminho);
        else
            await relatorio.GerarRelatorioVendasPdfAsync(vendas, inicio, fim, caminho);

        NotificarSucesso(caminho);
    }

    private async Task GerarEstoqueAsync(Func<IProdutoService, Task<IEnumerable<ProdutoDto>>> obterProdutos)
    {
        var sufixo = _tipoRelatorio switch
        {
            "EstoqueBaixo" => "EstoqueBaixo",
            "SemEstoque" => "SemEstoque",
            _ => "Estoque"
        };
        var excel = ExportarExcel;
        if (!TentarObterCaminhoSalvar($"{sufixo}_{DateTime.Today:yyyyMMdd}", excel, out var caminho))
            return;

        var produtoService = _serviceProvider.GetRequiredService<IProdutoService>();
        var produtos = await obterProdutos(produtoService);
        var relatorio = _serviceProvider.GetRequiredService<IRelatorioService>();

        if (excel)
            await relatorio.GerarRelatorioEstoqueExcelAsync(produtos, caminho);
        else
            await relatorio.GerarRelatorioEstoquePdfAsync(produtos, caminho);

        NotificarSucesso(caminho);
    }

    /// <summary>Janela de "vencendo em breve" para o relatório "Próximos da Validade" —
    /// mesmo prazo usado em vários mercados/farmácias como alerta padrão de giro.</summary>
    private const int DiasLimiteValidadeProxima = 15;

    private async Task GerarValidadeProximaAsync()
    {
        var excel = ExportarExcel;
        if (!TentarObterCaminhoSalvar($"ValidadeProxima_{DateTime.Today:yyyyMMdd}", excel, out var caminho))
            return;

        var produtos = await _serviceProvider.GetRequiredService<IProdutoService>()
            .ObterProximosDaValidadeAsync(DiasLimiteValidadeProxima);
        var relatorio = _serviceProvider.GetRequiredService<IRelatorioService>();

        if (excel)
            await relatorio.GerarRelatorioValidadeExcelAsync(produtos, DiasLimiteValidadeProxima, caminho);
        else
            await relatorio.GerarRelatorioValidadePdfAsync(produtos, DiasLimiteValidadeProxima, caminho);

        NotificarSucesso(caminho);
    }

    private async Task GerarTabelaPrecosAsync(decimal acrescimo, string nomeArquivo, string titulo, string subtituloBase)
    {
        var excel = ExportarExcel;
        if (!TentarObterCaminhoSalvar($"{nomeArquivo}_{DateTime.Today:yyyyMMdd}", excel, out var caminho))
            return;

        var produtos = await _serviceProvider.GetRequiredService<IProdutoService>().ObterTodosAsync();
        var relatorio = _serviceProvider.GetRequiredService<IRelatorioService>();
        var subtitulo = $"{subtituloBase} — Gerado em: {DateTime.Now:dd/MM/yyyy HH:mm}";

        if (excel)
            await relatorio.GerarTabelaPrecosExcelAsync(produtos, titulo, acrescimo, caminho);
        else
            await relatorio.GerarTabelaPrecosPdfAsync(produtos, titulo, subtitulo, acrescimo, caminho);

        NotificarSucesso(caminho);
    }

    private async Task GerarVendasExternasAsync()
    {
        var (inicio, fim) = ObterPeriodo();
        var excel = ExportarExcel;
        if (!TentarObterCaminhoSalvar($"VendasExternas_{inicio:yyyyMMdd}_{fim:yyyyMMdd}", excel, out var caminho))
            return;

        var analytics = _serviceProvider.GetRequiredService<IRelatorioAnalyticsService>();
        var linhas = await analytics.ObterLinhasVendasExternasAsync(inicio, fim);
        var relatorio = _serviceProvider.GetRequiredService<IRelatorioService>();

        if (excel)
            await relatorio.GerarRelatorioVendasExternasExcelAsync(linhas, inicio, fim, caminho);
        else
            await relatorio.GerarRelatorioVendasExternasPdfAsync(linhas, inicio, fim, caminho);

        NotificarSucesso(caminho);
    }

    private async Task GerarVendasConsolidadasAsync()
    {
        var (inicio, fim) = ObterPeriodo();
        var excel = ExportarExcel;
        if (!TentarObterCaminhoSalvar($"VendasConsolidadas_{inicio:yyyyMMdd}_{fim:yyyyMMdd}", excel, out var caminho))
            return;

        var analytics = _serviceProvider.GetRequiredService<IRelatorioAnalyticsService>();
        var linhas = await analytics.ObterVendasConsolidadasAsync(inicio, fim);
        var relatorio = _serviceProvider.GetRequiredService<IRelatorioService>();

        if (excel)
            await relatorio.GerarRelatorioVendasConsolidadasExcelAsync(linhas, inicio, fim, caminho);
        else
            await relatorio.GerarRelatorioVendasConsolidadasPdfAsync(linhas, inicio, fim, caminho);

        NotificarSucesso(caminho);
    }

    private async Task GerarAnaliseGiroAsync()
    {
        var (inicio, fim) = ObterPeriodo();
        var tipo = ObterTipoAnaliseGiroSelecionado();
        var excel = ExportarExcel;
        var sufixo = tipo switch
        {
            TipoAnaliseGiroProduto.MaisVendidos => "MaisVendidos",
            TipoAnaliseGiroProduto.MenosVendidos => "MenosVendidos",
            _ => "Encalhados"
        };

        if (!TentarObterCaminhoSalvar($"GiroProdutos_{sufixo}_{inicio:yyyyMMdd}_{fim:yyyyMMdd}", excel, out var caminho))
            return;

        var analytics = _serviceProvider.GetRequiredService<IRelatorioAnalyticsService>();
        var relatorio = _serviceProvider.GetRequiredService<IRelatorioService>();

        if (tipo == TipoAnaliseGiroProduto.NuncaVendidos)
        {
            var encalhados = await analytics.ObterProdutosEncalhadosAsync(inicio, fim);
            if (excel)
                await relatorio.GerarRelatorioProdutosEncalhadosExcelAsync(encalhados, inicio, fim, caminho);
            else
                await relatorio.GerarRelatorioProdutosEncalhadosPdfAsync(encalhados, inicio, fim, caminho);
        }
        else
        {
            var ranking = await analytics.ObterRankingProdutosAsync(inicio, fim, tipo);
            var titulo = tipo == TipoAnaliseGiroProduto.MaisVendidos
                ? "Produtos Mais Vendidos"
                : "Produtos Menos Vendidos";

            if (excel)
                await relatorio.GerarRelatorioRankingProdutosExcelAsync(ranking, titulo, inicio, fim, caminho);
            else
                await relatorio.GerarRelatorioRankingProdutosPdfAsync(ranking, titulo, inicio, fim, caminho);
        }

        NotificarSucesso(caminho);
    }

    private TipoAnaliseGiroProduto ObterTipoAnaliseGiroSelecionado()
    {
        var tag = (CmbTipoGiro.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return tag switch
        {
            "MenosVendidos" => TipoAnaliseGiroProduto.MenosVendidos,
            "NuncaVendidos" => TipoAnaliseGiroProduto.NuncaVendidos,
            _ => TipoAnaliseGiroProduto.MaisVendidos
        };
    }
}
