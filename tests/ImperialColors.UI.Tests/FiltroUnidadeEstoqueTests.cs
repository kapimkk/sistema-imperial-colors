using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Constants;
using ImperialColors.UI.Services;
using ImperialColors.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.IO;
using Xunit;

namespace ImperialColors.UI.Tests;

/// <summary>
/// Filtro por unidade de medida na lista de estoque (BD, GL, LA...). Ele é PERSISTENTE: quem
/// escolhe "só BD" vê só BD em tudo que pesquisar, e só deixa de ser assim ao clicar em
/// "Limpar filtro" — inclusive depois de trocar de tela e de fechar o sistema.
/// </summary>
public class FiltroUnidadeEstoqueTests
{
    // ---------- Armazenamento (arquivo) ----------

    private sealed class PastaTemporaria : IDisposable
    {
        public string Raiz { get; } = Path.Combine(Path.GetTempPath(), "ic_filtro_" + Guid.NewGuid().ToString("N"));
        public string Arquivo => Path.Combine(Raiz, "filtro_estoque.json");
        public PastaTemporaria() => Directory.CreateDirectory(Raiz);
        public void Dispose() { try { Directory.Delete(Raiz, true); } catch (IOException) { } }
    }

    [Fact]
    public void Store_GravaELeDeVolta()
    {
        using var pasta = new PastaTemporaria();

        new FiltroUnidadeEstoqueStore(pasta.Arquivo).Salvar(["BD", "GL"]);

        // Instância nova, como no próximo dia de trabalho.
        Assert.Equal(["BD", "GL"], new FiltroUnidadeEstoqueStore(pasta.Arquivo).Carregar().ToArray());
    }

    [Fact]
    public void Store_SemArquivo_DevolveSemFiltro()
    {
        using var pasta = new PastaTemporaria();

        Assert.Empty(new FiltroUnidadeEstoqueStore(pasta.Arquivo).Carregar());
    }

    /// <summary>Limpar o filtro apaga o que estava guardado: na próxima abertura, lista inteira.</summary>
    [Fact]
    public void Store_SalvarVazio_ApagaOFiltroGuardado()
    {
        using var pasta = new PastaTemporaria();
        var store = new FiltroUnidadeEstoqueStore(pasta.Arquivo);
        store.Salvar(["BD"]);

        store.Salvar([]);

        Assert.False(File.Exists(pasta.Arquivo));
        Assert.Empty(store.Carregar());
    }

    /// <summary>Arquivo corrompido ou editado à mão não pode derrubar a tela de estoque: o pior
    /// resultado é o filtro voltar vazio.</summary>
    [Theory]
    [InlineData("isso não é json")]
    [InlineData("{ \"Unidades\": 42 }")]
    [InlineData("")]
    public void Store_ArquivoCorrompido_DevolveSemFiltro(string conteudo)
    {
        using var pasta = new PastaTemporaria();
        File.WriteAllText(pasta.Arquivo, conteudo);

        Assert.Empty(new FiltroUnidadeEstoqueStore(pasta.Arquivo).Carregar());
    }

    /// <summary>Sigla que não existe mais (ou foi digitada errada) não pode filtrar tudo para
    /// fora.</summary>
    [Fact]
    public void Store_IgnoraSiglasQueNaoExistem_ENormaliza()
    {
        using var pasta = new PastaTemporaria();
        File.WriteAllText(pasta.Arquivo, "{ \"Unidades\": [\"bd\", \"XYZ\", \"BD\", \"  gl \"] }");

        Assert.Equal(["BD", "GL"], new FiltroUnidadeEstoqueStore(pasta.Arquivo).Carregar().ToArray());
    }

    /// <summary>Guardar o filtro é conveniência: pasta impossível de criar não pode estourar.</summary>
    [Fact]
    public void Store_NaoConseguirGravar_NaoEstoura()
    {
        using var pasta = new PastaTemporaria();
        var arquivoNoLugarDaPasta = Path.Combine(pasta.Raiz, "arquivo.txt");
        File.WriteAllText(arquivoNoLugarDaPasta, "x");

        var excecao = Record.Exception(() =>
            new FiltroUnidadeEstoqueStore(Path.Combine(arquivoNoLugarDaPasta, "sub", "f.json")).Salvar(["BD"]));

        Assert.Null(excecao);
    }

    // ---------- ViewModel ----------

    private sealed class StoreEmMemoria : IFiltroUnidadeEstoqueStore
    {
        public List<string> Guardado { get; set; } = new();
        public int Gravacoes { get; private set; }

        public IReadOnlyList<string> Carregar() => Guardado.ToList();

        public void Salvar(IReadOnlyCollection<string> unidades)
        {
            Guardado = unidades.ToList();
            Gravacoes++;
        }
    }

    private sealed record ChamadaBusca(string? Termo, bool Promocao, int Pagina, string[]? Unidades);

    private sealed class Cenario
    {
        public required Mock<IProdutoService> Servico { get; init; }
        public required List<ChamadaBusca> Chamadas { get; init; }
        public int TotalDeChamadas { get { lock (Chamadas) return Chamadas.Count; } }
        public ChamadaBusca Ultima { get { lock (Chamadas) return Chamadas[^1]; } }
    }

    private static Cenario CriarServico(int totalPaginas = 1)
    {
        var chamadas = new List<ChamadaBusca>();
        var servico = new Mock<IProdutoService>();
        servico
            .Setup(s => s.ObterPaginadoAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int pagina, int porPagina, string? termo, bool promo, IReadOnlyCollection<string>? unidades, CancellationToken _) =>
            {
                lock (chamadas)
                    chamadas.Add(new ChamadaBusca(termo, promo, pagina, unidades?.ToArray()));

                return new PaginacaoResultadoDto<ProdutoDto>
                {
                    Itens = new List<ProdutoDto>(),
                    PaginaAtual = pagina,
                    ItensPorPagina = porPagina,
                    TotalItens = totalPaginas * porPagina
                };
            });
        return new Cenario { Servico = servico, Chamadas = chamadas };
    }

    private static ProdutoViewModel CriarVm(Cenario cenario, IFiltroUnidadeEstoqueStore? store = null)
        => new(cenario.Servico.Object, Mock.Of<IServiceScopeFactory>(), store);

    /// <summary>A busca espera 300 ms (debounce) e roda em segundo plano: aguarda a condição.</summary>
    private static async Task EsperarAsync(Func<bool> condicao, int milissegundos = 5000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(milissegundos);
        while (!condicao())
        {
            if (DateTime.UtcNow > limite)
                throw new TimeoutException("A condição esperada não aconteceu a tempo.");
            await Task.Delay(20);
        }
    }

    private static OpcaoUnidadeFiltro Opcao(ProdutoViewModel vm, string sigla)
        => vm.OpcoesUnidade.Single(o => o.Sigla == sigla);

    [Fact]
    public void ListaOferecesTodasAsUnidadesDisponiveis_TodasDesmarcadas()
    {
        var vm = CriarVm(CriarServico());

        // As mesmas do cadastro de produto (BD, GL, LA, BA, UN, LT, RL, CX, PCT...).
        Assert.Equal(UnidadesMedida.Todas.OrderBy(u => u), vm.OpcoesUnidade.Select(o => o.Sigla).OrderBy(u => u));
        Assert.All(vm.OpcoesUnidade, o => Assert.False(o.Selecionada));
        Assert.False(vm.FiltroUnidadeAtivo);
        Assert.Equal("Unidade", vm.TextoFiltroUnidade);
        Assert.False(vm.LimparFiltroUnidadeCommand.CanExecute(null));
    }

    [Fact]
    public async Task MarcarUmaUnidade_FiltraABusca()
    {
        var cenario = CriarServico();
        var vm = CriarVm(cenario);

        Opcao(vm, "BD").Selecionada = true;
        await EsperarAsync(() => cenario.TotalDeChamadas >= 1);

        Assert.Equal(["BD"], cenario.Ultima.Unidades);
        Assert.True(vm.FiltroUnidadeAtivo);
        Assert.Equal("Unidade: BD", vm.TextoFiltroUnidade);
        Assert.True(vm.LimparFiltroUnidadeCommand.CanExecute(null));
    }

    [Fact]
    public async Task MarcarVariasUnidades_FiltraPorTodas()
    {
        var cenario = CriarServico();
        var vm = CriarVm(cenario);

        Opcao(vm, "BD").Selecionada = true;
        Opcao(vm, "GL").Selecionada = true;
        await EsperarAsync(() => cenario.TotalDeChamadas >= 1 && cenario.Ultima.Unidades?.Length == 2);

        Assert.Equal(new[] { "BD", "GL" }, cenario.Ultima.Unidades!.OrderBy(u => u));
        Assert.Equal("Unidade: GL, BD", vm.TextoFiltroUnidade); // na ordem da lista
    }

    /// <summary>Quatro ou mais não cabem no botão: vira contagem.</summary>
    [Fact]
    public void MuitasUnidadesMarcadas_ResumemNoBotao()
    {
        var store = new StoreEmMemoria { Guardado = ["UN", "GL", "LT", "BD"] };
        var vm = CriarVm(CriarServico(), store);

        Assert.Equal("Unidade: 4 selecionadas", vm.TextoFiltroUnidade);
    }

    /// <summary>O ponto central: o filtro fica ligado em tudo que o operador faz na tela.</summary>
    [Fact]
    public async Task Filtro_ContinuaLigadoNaPesquisaNaPaginacaoEnoAtualizar()
    {
        var cenario = CriarServico(totalPaginas: 3);
        var vm = CriarVm(cenario);
        Opcao(vm, "BD").Selecionada = true;
        await EsperarAsync(() => cenario.TotalDeChamadas >= 1);

        // Pesquisar
        vm.TermoBusca = "coral";
        await EsperarAsync(() => cenario.Ultima.Termo == "coral");
        Assert.Equal(["BD"], cenario.Ultima.Unidades);

        // "Apenas em promoção" junto
        vm.ApenasEmPromocao = true;
        await EsperarAsync(() => cenario.Ultima.Promocao);
        Assert.Equal(["BD"], cenario.Ultima.Unidades);

        // Próxima página (só existe depois de a busca terminar e informar o total)
        await EsperarAsync(() => !vm.Carregando && vm.TotalPaginas == 3);
        var antes = cenario.TotalDeChamadas;
        vm.PaginaProximaCommand.Execute(null);
        await EsperarAsync(() => cenario.TotalDeChamadas > antes);
        Assert.Equal(2, cenario.Ultima.Pagina);
        Assert.Equal(["BD"], cenario.Ultima.Unidades);

        // Atualizar
        antes = cenario.TotalDeChamadas;
        vm.AtualizarCommand.Execute(null);
        await EsperarAsync(() => cenario.TotalDeChamadas > antes);
        Assert.Equal(["BD"], cenario.Ultima.Unidades);
    }

    /// <summary>A página 3 de "todas" não tem relação com a página 3 de "só BD".</summary>
    [Fact]
    public async Task MudarOFiltro_VoltaParaAPrimeiraPagina()
    {
        var cenario = CriarServico(totalPaginas: 5);
        var vm = CriarVm(cenario);
        await vm.CarregarAsync();
        await EsperarAsync(() => cenario.TotalDeChamadas >= 1);
        vm.PaginaAtual = 3;
        await EsperarAsync(() => vm.PaginaAtual == 3);

        Opcao(vm, "BD").Selecionada = true;
        await EsperarAsync(() => cenario.Ultima.Unidades is not null);

        Assert.Equal(1, cenario.Ultima.Pagina);
        Assert.Equal(1, vm.PaginaAtual);
    }

    /// <summary>
    /// Persistência: o ViewModel é recriado a cada visita à tela de estoque. O filtro tem que
    /// voltar como o operador deixou — e a primeira busca já sai filtrada.
    /// </summary>
    [Fact]
    public async Task Filtro_SobreviveATrocarDeTela()
    {
        var store = new StoreEmMemoria();
        var primeiraVisita = CriarVm(CriarServico(), store);
        Opcao(primeiraVisita, "BD").Selecionada = true;
        Assert.Equal(["BD"], store.Guardado);

        // Saiu da tela e voltou: ViewModel novo, mesmo armazenamento.
        var cenario = CriarServico();
        var segundaVisita = CriarVm(cenario, store);
        await segundaVisita.CarregarAsync();
        await EsperarAsync(() => cenario.TotalDeChamadas >= 1);

        Assert.True(Opcao(segundaVisita, "BD").Selecionada);
        Assert.Equal("Unidade: BD", segundaVisita.TextoFiltroUnidade);
        Assert.Equal(["BD"], cenario.Ultima.Unidades);
    }

    /// <summary>Persistência em disco: fechar e abrir o sistema também não tira o filtro.</summary>
    [Fact]
    public async Task Filtro_SobreviveAFecharEAbrirOSistema()
    {
        using var pasta = new PastaTemporaria();
        var vm1 = CriarVm(CriarServico(), new FiltroUnidadeEstoqueStore(pasta.Arquivo));
        Opcao(vm1, "LA").Selecionada = true;

        // "Dia seguinte": tudo novo, só o arquivo é o mesmo.
        var cenario = CriarServico();
        var vm2 = CriarVm(cenario, new FiltroUnidadeEstoqueStore(pasta.Arquivo));
        await vm2.CarregarAsync();
        await EsperarAsync(() => cenario.TotalDeChamadas >= 1);

        Assert.True(Opcao(vm2, "LA").Selecionada);
        Assert.Equal(["LA"], cenario.Ultima.Unidades);
    }

    [Fact]
    public async Task LimparFiltro_DesmarcaTudo_VoltaALista_EApagaOGuardado()
    {
        var cenario = CriarServico();
        var store = new StoreEmMemoria();
        var vm = CriarVm(cenario, store);
        Opcao(vm, "BD").Selecionada = true;
        Opcao(vm, "GL").Selecionada = true;
        await EsperarAsync(() => cenario.TotalDeChamadas >= 1 && cenario.Ultima.Unidades?.Length == 2);

        vm.LimparFiltroUnidadeCommand.Execute(null);
        await EsperarAsync(() => cenario.Ultima.Unidades is null);

        Assert.All(vm.OpcoesUnidade, o => Assert.False(o.Selecionada));
        Assert.False(vm.FiltroUnidadeAtivo);
        Assert.Equal("Unidade", vm.TextoFiltroUnidade);
        Assert.Empty(store.Guardado);
        Assert.False(vm.LimparFiltroUnidadeCommand.CanExecute(null));
    }

    /// <summary>
    /// Desmarcar várias opções de uma vez é UMA busca e UMA gravação — sem isso cada opção
    /// dispararia a sua e a tela piscaria entre resultados intermediários.
    /// </summary>
    [Fact]
    public async Task LimparFiltro_ComVariasMarcadas_FazUmaBuscaSo()
    {
        var cenario = CriarServico();
        var store = new StoreEmMemoria();
        var vm = CriarVm(cenario, store);
        Opcao(vm, "BD").Selecionada = true;
        Opcao(vm, "GL").Selecionada = true;
        Opcao(vm, "LA").Selecionada = true;
        await EsperarAsync(() => cenario.TotalDeChamadas >= 1 && cenario.Ultima.Unidades?.Length == 3);
        await Task.Delay(500);
        var buscasAntes = cenario.TotalDeChamadas;
        var gravacoesAntes = store.Gravacoes;

        vm.LimparFiltroUnidadeCommand.Execute(null);
        await EsperarAsync(() => cenario.TotalDeChamadas > buscasAntes);
        await Task.Delay(700);

        Assert.Equal(buscasAntes + 1, cenario.TotalDeChamadas);
        Assert.Equal(gravacoesAntes + 1, store.Gravacoes);
    }

    [Fact]
    public async Task Cabecalho_DizQueOTotalEDoFiltro()
    {
        var cenario = CriarServico();
        var vm = CriarVm(cenario);
        vm.TotalProdutos = 0;
        Assert.Equal("0 produtos cadastrados", vm.ResumoTotalProdutos);

        Opcao(vm, "BD").Selecionada = true;
        await EsperarAsync(() => cenario.TotalDeChamadas >= 1);
        await EsperarAsync(() => vm.TotalProdutos > 0);

        Assert.Equal($"{vm.TotalProdutos} produtos na unidade BD", vm.ResumoTotalProdutos);

        Opcao(vm, "GL").Selecionada = true;
        await EsperarAsync(() => cenario.Ultima.Unidades?.Length == 2);
        await EsperarAsync(() => vm.ResumoTotalProdutos.Contains("unidades"));

        Assert.EndsWith("nas unidades GL, BD", vm.ResumoTotalProdutos);
    }

    // ---------- Leitor de código de barras ----------

    private static ProdutoDto Produto(string unidade) => new()
    {
        Id = 1, Nome = "Tinta Acrílica", CodigoInterno = "TAB-001", CodigoBarras = "7891234567890", Unidade = unidade
    };

    /// <summary>
    /// Leu um código que existe, mas o produto é de outra unidade que não a do filtro: a lista
    /// ficaria vazia e pareceria "não encontrado". Em vez disso o operador é avisado do porquê.
    /// </summary>
    [Fact]
    public async Task Leitura_DeProdutoForaDoFiltro_AvisaEmVezDeParecerNaoEncontrado()
    {
        var cenario = CriarServico();
        cenario.Servico.Setup(s => s.ObterPorCodigoBarrasAsync("7891234567890")).ReturnsAsync(Produto("LT"));
        var vm = CriarVm(cenario, new StoreEmMemoria { Guardado = ["BD"] });

        var encontrado = await vm.ProcessarLeituraCodigoBarrasAsync("7891234567890");

        Assert.True(encontrado); // existe: a tela não pode dizer "não encontrado"
        Assert.True(vm.TemAvisoFiltroUnidade);
        Assert.Contains("LT", vm.AvisoFiltroUnidade);
        Assert.Contains("BD", vm.AvisoFiltroUnidade);
        Assert.Equal(string.Empty, vm.TermoBusca);
    }

    [Fact]
    public async Task Leitura_DeProdutoDentroDoFiltro_PesquisaNormalmente()
    {
        var cenario = CriarServico();
        cenario.Servico.Setup(s => s.ObterPorCodigoBarrasAsync("7891234567890")).ReturnsAsync(Produto("BD"));
        var vm = CriarVm(cenario, new StoreEmMemoria { Guardado = ["BD"] });

        var encontrado = await vm.ProcessarLeituraCodigoBarrasAsync("7891234567890");

        Assert.True(encontrado);
        Assert.False(vm.TemAvisoFiltroUnidade);
        Assert.Equal("7891234567890", vm.TermoBusca);
    }

    [Fact]
    public async Task Leitura_SemFiltro_NaoAvisaDeNada()
    {
        var cenario = CriarServico();
        cenario.Servico.Setup(s => s.ObterPorCodigoBarrasAsync("7891234567890")).ReturnsAsync(Produto("LT"));
        var vm = CriarVm(cenario);

        var encontrado = await vm.ProcessarLeituraCodigoBarrasAsync("7891234567890");

        Assert.True(encontrado);
        Assert.False(vm.TemAvisoFiltroUnidade);
    }

    /// <summary>O aviso de "fora do filtro" some quando o operador faz outra busca.</summary>
    [Fact]
    public async Task AvisoDeForaDoFiltro_SomeNaProximaBusca()
    {
        var cenario = CriarServico();
        cenario.Servico.Setup(s => s.ObterPorCodigoBarrasAsync("7891234567890")).ReturnsAsync(Produto("LT"));
        var vm = CriarVm(cenario, new StoreEmMemoria { Guardado = ["BD"] });
        await vm.ProcessarLeituraCodigoBarrasAsync("7891234567890");
        Assert.True(vm.TemAvisoFiltroUnidade);

        vm.TermoBusca = "coral";
        await EsperarAsync(() => cenario.TotalDeChamadas >= 1);
        await EsperarAsync(() => !vm.TemAvisoFiltroUnidade);

        Assert.False(vm.TemAvisoFiltroUnidade);
    }
}
