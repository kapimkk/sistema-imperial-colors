using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Services;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Interfaces;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Regressão do XSD_VALIDATION "incomplete content... expected 'pICMS'": um produto com CST
/// de ICMS '00' (ou 20/51/90) cadastrado mas sem alíquota preenchida saía com
/// <c>ItemNotaFiscalDto.BaseIcms</c> nulo (a condição antiga era "só preenche vBC se a
/// alíquota estiver cadastrada"), então o grupo ICMS00 do XML ficava sem vBC/pICMS/vICMS —
/// campos obrigatórios no leiaute NFe — e a SEFAZ rejeitava a nota. <c>BaseIcms</c> agora
/// depende só do CST (o valor do item é sempre a base ad-valorem quando o CST exige o grupo),
/// não da alíquota estar cadastrada — quem bloqueia a emissão nesse cenário é
/// <see cref="Validation.NotaFiscalValidator"/> (ver NotaFiscalValidatorTests).
/// </summary>
public class NotaFiscalServiceMontarItemTests
{
    private const int ProdutoId = 1;
    private const decimal PrecoVenda = 100m;
    private const decimal Quantidade = 1m;

    private static NotaFiscalService CriarServico(
        TributacaoProduto? tributacao, ConfiguracaoFiscalEmpresaDto? empresa = null, string crtAtual = "3")
        => CriarServico(new Produto
        {
            Id = ProdutoId,
            CodigoInterno = "P001",
            Nome = "Galão de teste",
            Unidade = "UN",
            PrecoVenda = PrecoVenda
        }, tributacao, empresa, crtAtual);

    private static NotaFiscalService CriarServico(
        Produto produto, TributacaoProduto? tributacao, ConfiguracaoFiscalEmpresaDto? empresa = null, string crtAtual = "3")
    {
        var produtoRepo = new Mock<IProdutoRepository>();
        produtoRepo.Setup(r => r.ObterPorIdAsync(produto.Id)).ReturnsAsync(produto);

        var tributacaoRepo = new Mock<ITributacaoProdutoRepository>();
        tributacaoRepo.Setup(r => r.ObterPorProdutoIdAsync(produto.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tributacao);

        var configuracaoFiscal = new Mock<IConfiguracaoFiscalService>();
        configuracaoFiscal.Setup(s => s.ObterConfiguracaoEmpresaAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(empresa ?? new ConfiguracaoFiscalEmpresaDto());
        // CRT "atual" (fetch em tempo real) — deliberadamente configurável separado da nota
        // sendo sincronizada, para simular um regime tributário que mudou depois que o
        // rascunho foi criado com outro CRT.
        configuracaoFiscal.Setup(s => s.ObterCodigoCrtAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(crtAtual);

        return new NotaFiscalService(
            Mock.Of<INotaFiscalRepository>(),
            Mock.Of<IVendaRepository>(),
            Mock.Of<IClienteRepository>(),
            produtoRepo.Object,
            tributacaoRepo.Object,
            Mock.Of<ITributacaoCategoriaRepository>(),
            Mock.Of<IRepository<NaturezaOperacao>>(),
            configuracaoFiscal.Object,
            Mock.Of<IFiscalApiClient>());
    }

    [Theory]
    [InlineData("00")]
    [InlineData("20")]
    [InlineData("51")]
    [InlineData("90")]
    public async Task MontarItemAPartirDeProdutoAsync_CstTributacaoIntegralSemAliquotaCadastrada_PreencheBaseIcmsMesmoAssim(string cst)
    {
        var servico = CriarServico(new TributacaoProduto { ProdutoId = ProdutoId, CstIcms = cst, AliquotaIcms = null });

        var item = await servico.MontarItemAPartirDeProdutoAsync(ProdutoId, Quantidade, interestadual: false);

        // BaseIcms (vBC) tem que sair preenchido mesmo sem alíquota — é isso que garante que o
        // NotaFiscalPayloadBuilder tenta montar o grupo ICMS no XML; quem bloqueia a emissão por
        // faltar a alíquota é o NotaFiscalValidator, não um vBC silenciosamente omitido.
        Assert.Equal(PrecoVenda * Quantidade, item.BaseIcms);
        Assert.Null(item.AliquotaIcms);
        Assert.Contains(item.Avisos, a => a.Contains("exige alíquota"));
    }

    [Fact]
    public async Task MontarItemAPartirDeProdutoAsync_Cst00ComAliquotaCadastrada_PreencheBaseICMSEValorIcms()
    {
        var servico = CriarServico(new TributacaoProduto
        {
            ProdutoId = ProdutoId,
            CstIcms = "00",
            AliquotaIcms = 18m,
            CstPis = "07", // não tributado — evita aviso de "sem CST cadastrado" que poluiria a asserção de Avisos
            CstCofins = "07"
        });

        var item = await servico.MontarItemAPartirDeProdutoAsync(ProdutoId, Quantidade, interestadual: false);

        Assert.Equal(PrecoVenda * Quantidade, item.BaseIcms);
        Assert.Equal(18m, item.ValorIcms); // 100 * 18% = 18
        Assert.Empty(item.Avisos);
    }

    [Fact]
    public async Task MontarItemAPartirDeProdutoAsync_CsosnSimplesNacional_NaoPreencheBaseIcms()
    {
        // CSOSN nunca destaca vICMS na nota — imposto embutido no preço, sem grupo vBC/pICMS.
        var servico = CriarServico(new TributacaoProduto { ProdutoId = ProdutoId, CsosnIcms = "102" });

        var item = await servico.MontarItemAPartirDeProdutoAsync(ProdutoId, Quantidade, interestadual: false);

        Assert.Null(item.BaseIcms);
        Assert.Equal(0m, item.ValorIcms); // CalcularIcms nunca atribui VIcms pra CSOSN — fica no default (0), não null
    }

    [Fact]
    public async Task MontarItemAPartirDeProdutoAsync_SemTributacaoCadastrada_UsaCstIcmsPadraoDaEmpresaEPreencheBaseIcms()
    {
        var empresa = new ConfiguracaoFiscalEmpresaDto { CstIcmsPadrao = "00" };
        var servico = CriarServico(tributacao: null, empresa: empresa);

        var item = await servico.MontarItemAPartirDeProdutoAsync(ProdutoId, Quantidade, interestadual: false);

        Assert.Equal("00", item.CstIcms);
        Assert.Equal(PrecoVenda * Quantidade, item.BaseIcms);
    }

    /// <summary>
    /// A alíquota tem que seguir o mesmo dono do CST. Quando o item herda o CST da Regra
    /// Geral da empresa e a alíquota continua vindo (nula) do produto, o resultado é uma nota
    /// eternamente bloqueada por "falta alíquota" sem lugar onde cadastrá-la — o produto não
    /// tem o CST que justificaria o campo.
    /// </summary>
    [Fact]
    public async Task MontarItemAPartirDeProdutoAsync_SemTributacaoCadastrada_UsaTambemAliquotaIcmsPadraoDaEmpresa()
    {
        var empresa = new ConfiguracaoFiscalEmpresaDto { CstIcmsPadrao = "00", AliquotaIcmsPadrao = 18m };
        var servico = CriarServico(tributacao: null, empresa: empresa);

        var item = await servico.MontarItemAPartirDeProdutoAsync(ProdutoId, Quantidade, interestadual: false);

        Assert.Equal(18m, item.AliquotaIcms);
        Assert.Equal(18m, item.ValorIcms); // 100 × 18%
        Assert.DoesNotContain(item.Avisos, a => a.Contains("exige alíquota"));
    }

    /// <summary>A alíquota do produto tem precedência: quem cadastrou a tributação própria
    /// não pode ser sobrescrito pelo padrão da empresa.</summary>
    [Fact]
    public async Task MontarItemAPartirDeProdutoAsync_ProdutoComTributacaoPropria_IgnoraAliquotaPadraoDaEmpresa()
    {
        var empresa = new ConfiguracaoFiscalEmpresaDto { CstIcmsPadrao = "00", AliquotaIcmsPadrao = 18m };
        var servico = CriarServico(
            new TributacaoProduto { ProdutoId = ProdutoId, CstIcms = "00", AliquotaIcms = 12m },
            empresa);

        var item = await servico.MontarItemAPartirDeProdutoAsync(ProdutoId, Quantidade, interestadual: false);

        Assert.Equal(12m, item.AliquotaIcms);
        Assert.Equal(12m, item.ValorIcms);
    }

    /// <summary>Regressão: vBC de PIS/COFINS nunca era preenchido — o payload saía com
    /// pPIS/vPIS sem vBC e o grupo PISAliq/COFINSAliq era rejeitado por incompleto.</summary>
    [Fact]
    public async Task MontarItemAPartirDeProdutoAsync_PisCofinsTributados_PreencheBasesDeCalculo()
    {
        var servico = CriarServico(new TributacaoProduto
        {
            ProdutoId = ProdutoId,
            CsosnIcms = "102",
            CstPis = "01",
            AliquotaPis = 1.65m,
            CstCofins = "01",
            AliquotaCofins = 7.6m
        });

        var item = await servico.MontarItemAPartirDeProdutoAsync(ProdutoId, Quantidade, interestadual: false);

        Assert.Equal(100m, item.BasePis);
        Assert.Equal(1.65m, item.ValorPis);
        Assert.Equal(100m, item.BaseCofins);
        Assert.Equal(7.6m, item.ValorCofins);
    }

    [Fact]
    public async Task MontarItemAPartirDeProdutoAsync_PisNaoTributado_NaoPreencheBaseDeCalculo()
    {
        var servico = CriarServico(new TributacaoProduto
        {
            ProdutoId = ProdutoId,
            CsosnIcms = "102",
            CstPis = "07",
            CstCofins = "07"
        });

        var item = await servico.MontarItemAPartirDeProdutoAsync(ProdutoId, Quantidade, interestadual: false);

        Assert.Null(item.BasePis);
        Assert.Null(item.BaseCofins);
    }

    /// <summary>CST 20 reduz a base: antes o ICMS era calculado sobre o valor cheio do item
    /// (imposto destacado a maior) e o pRedBC nem chegava ao XML.</summary>
    [Fact]
    public async Task MontarItemAPartirDeProdutoAsync_Cst20ComReducaoDeBase_AplicaReducaoEPropagaPercentual()
    {
        var servico = CriarServico(new TributacaoProduto
        {
            ProdutoId = ProdutoId,
            CstIcms = "20",
            AliquotaIcms = 18m,
            ReducaoBaseCalculo = 40m
        });

        var item = await servico.MontarItemAPartirDeProdutoAsync(ProdutoId, Quantidade, interestadual: false);

        Assert.Equal(40m, item.ReducaoBaseCalculo);
        Assert.Equal(60m, item.BaseIcms);   // 100 − 40%
        Assert.Equal(10.80m, item.ValorIcms); // 60 × 18%
    }

    // ===== SincronizarTributacaoComCadastroAtualAsync — regressão do caso real reportado:
    // rascunho criado com CRT '1' e item com CST '00' congelados; depois o regime mudou (ou
    // a tributação do produto foi corrigida) e a nota nunca acompanhava sem um clique manual
    // em "Atualizar" por item (o CRT nem tinha essa opção). =====

    [Fact]
    public async Task SincronizarTributacaoComCadastroAtualAsync_AtualizaCrtDaNotaComORegimeAtual()
    {
        var servico = CriarServico(tributacao: null, crtAtual: "3");
        var nota = new NotaFiscalDto { Crt = "1", Itens = new List<ItemNotaFiscalDto>() };

        var sincronizada = await servico.SincronizarTributacaoComCadastroAtualAsync(nota);

        Assert.Equal("3", sincronizada.Crt);
    }

    [Fact]
    public async Task SincronizarTributacaoComCadastroAtualAsync_ItemComCstCongelado_CorrigeParaCsosnAtualDoProdutoSemAlterarPrecoOuQuantidade()
    {
        // Cenário real: nota criada com CST '00' congelado (época em que a Regra Geral da
        // empresa ainda tinha um CST padrão cadastrado por engano). Depois o produto passou a
        // ter CSOSN 102 próprio — o item tem que refletir isso automaticamente.
        var servico = CriarServico(new TributacaoProduto { ProdutoId = ProdutoId, CsosnIcms = "102" }, crtAtual: "1");

        var nota = new NotaFiscalDto
        {
            Crt = "1",
            Itens = new List<ItemNotaFiscalDto>
            {
                new()
                {
                    ProdutoId = ProdutoId,
                    Descricao = "Galão de teste",
                    Quantidade = 3m,
                    ValorUnitario = 55m, // preço negociado na venda — diferente do cadastro atual
                    ValorTotal = 165m,
                    CstIcms = "00",      // congelado — incompatível com CSOSN/Simples
                    AliquotaIcms = null
                }
            }
        };

        var sincronizada = await servico.SincronizarTributacaoComCadastroAtualAsync(nota);
        var item = sincronizada.Itens[0];

        Assert.Null(item.CstIcms);
        Assert.Equal("102", item.CsosnIcms);
        // Preço/quantidade são dado comercial, não fiscal — não mudam sozinhos.
        Assert.Equal(3m, item.Quantidade);
        Assert.Equal(55m, item.ValorUnitario);
        Assert.Equal(165m, item.ValorTotal);
    }

    [Fact]
    public async Task SincronizarTributacaoComCadastroAtualAsync_ItemAvulsoSemProdutoId_NaoEhAlterado()
    {
        var servico = CriarServico(tributacao: null);
        var nota = new NotaFiscalDto
        {
            Crt = "3",
            Itens = new List<ItemNotaFiscalDto>
            {
                new() { ProdutoId = null, Descricao = "Item avulso", CstIcms = "40", ValorTotal = 10m }
            }
        };

        var sincronizada = await servico.SincronizarTributacaoComCadastroAtualAsync(nota);

        Assert.Equal("40", sincronizada.Itens[0].CstIcms);
    }

    [Fact]
    public async Task SincronizarTributacaoComCadastroAtualAsync_ProdutoNaoEncontrado_MantemItemCongeladoSemLancarExcecao()
    {
        // Produto pode ter sido excluído do estoque depois que o item foi lançado — não há
        // cadastro atual para sincronizar; a sincronização não deve quebrar por isso.
        var servico = CriarServico(tributacao: null);
        var nota = new NotaFiscalDto
        {
            Crt = "3",
            Itens = new List<ItemNotaFiscalDto>
            {
                new() { ProdutoId = 999, Descricao = "Produto excluído do estoque", CstIcms = "40", ValorTotal = 10m }
            }
        };

        var sincronizada = await servico.SincronizarTributacaoComCadastroAtualAsync(nota);

        Assert.Equal("40", sincronizada.Itens[0].CstIcms);
    }

    [Fact]
    public async Task SincronizarTributacaoComCadastroAtualAsync_ComCalculoAutomatico_RecalculaTotaisAPartirDosItensCorrigidos()
    {
        var servico = CriarServico(new TributacaoProduto { ProdutoId = ProdutoId, CstIcms = "00", AliquotaIcms = 10m });
        var nota = new NotaFiscalDto
        {
            Crt = "3",
            CalculoAutomatico = true,
            Itens = new List<ItemNotaFiscalDto>
            {
                new() { ProdutoId = ProdutoId, Descricao = "Galão de teste", ValorTotal = 100m, CompoeTotalNota = true }
            }
        };

        var sincronizada = await servico.SincronizarTributacaoComCadastroAtualAsync(nota);

        Assert.Equal(100m, sincronizada.VProd);
        Assert.Equal(10m, sincronizada.VIcms); // 100 × 10%
    }

    /// <summary>Abrir um rascunho em modo manual (CalculoAutomatico=false) não deve
    /// sobrescrever silenciosamente um total que o operador digitou à mão — só o ITEM é
    /// corrigido. A mesma preservação é aplicada na sincronização pré-emissão.</summary>
    [Fact]
    public async Task SincronizarTributacaoComCadastroAtualAsync_ComCalculoManual_PreservaTotalDigitadoMasCorrigeOItem()
    {
        var servico = CriarServico(new TributacaoProduto { ProdutoId = ProdutoId, CstIcms = "00", AliquotaIcms = 10m });
        var nota = new NotaFiscalDto
        {
            Crt = "3",
            CalculoAutomatico = false,
            VIcms = 999m,
            Itens = new List<ItemNotaFiscalDto>
            {
                new() { ProdutoId = ProdutoId, Descricao = "Galão de teste", ValorTotal = 100m, CompoeTotalNota = true }
            }
        };

        var sincronizada = await servico.SincronizarTributacaoComCadastroAtualAsync(nota);

        Assert.Equal(999m, sincronizada.VIcms);
        Assert.Equal(10m, sincronizada.Itens[0].ValorIcms);
    }
}
