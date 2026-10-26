using ImperialColors.Application.DTOs;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Infrastructure.Fiscal;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Testa <see cref="NotaFiscalPayloadBuilder"/> (internal, liberado para este assembly via
/// InternalsVisibleTo) contra as regras REAIS da pipeline de validação da API Fiscal —
/// conferidas no código-fonte local (PFCode\API-NF\src\Fiscal.Shared\Services\ValidationPipeline),
/// não só no GUIA_INTEGRACAO.md. Nasceu da investigação do erro "Rejeição Local: Inconsistências
/// detectadas na pipeline de validação." (HTTP 422): duas causas estruturais confirmadas —
/// (1) <c>FiscalLayoutValidator</c> exige textos fixos de Homologação em <c>dest.xNome</c>/
/// <c>prod.xProd</c> quando <c>tpAmb=2</c>; (2) <c>MathematicalValidator</c> soma os itens
/// REALMENTE enviados (não confia em totais pré-calculados) e rejeita qualquer divergência
/// de <c>total.ICMSTot.vNF</c>/<c>total.IBSCBSTot.gIBS.vIBS</c>/<c>gCBS.vCBS</c>.
/// </summary>
public class NotaFiscalPayloadBuilderTests
{
    private static EmitenteFiscalDto EmitenteValido() => new()
    {
        Cnpj = "13416624000136",
        RazaoSocial = "Imperial Colors Tintas Ltda",
        InscricaoEstadual = "1234567891",
        Crt = "3",
        Logradouro = "Avenida Central",
        Numero = "1000",
        Bairro = "Centro",
        CodigoMunicipioIbge = "4106902",
        Municipio = "Curitiba",
        Uf = "PR",
        Cep = "80530000"
    };

    private static ItemNotaFiscal ItemValido(int nItem = 1, decimal valorUnitario = 100m, decimal quantidade = 1m) => new()
    {
        NItem = nItem,
        CodigoProduto = "P001",
        Descricao = "Tinta Coral 18L",
        Ncm = "32091019",
        Cfop = "5102",
        Unidade = "UN",
        Quantidade = quantidade,
        ValorUnitario = valorUnitario,
        ValorTotal = valorUnitario * quantidade,
        Origem = "0",
        CstIcms = "00",
        BaseIcms = valorUnitario * quantidade,
        AliquotaIcms = 18m,
        ValorIcms = Math.Round(valorUnitario * quantidade * 0.18m, 2),
        CstPis = "01",
        CstCofins = "01",
        CstIbsCbs = "000",
        CClassTrib = "000001",
        BaseIbsCbs = valorUnitario * quantidade,
        AliquotaIbsUf = 0.10m,
        ValorIbsUf = Math.Round(valorUnitario * quantidade * 0.001m, 2),
        AliquotaCbs = 0.90m,
        ValorCbs = Math.Round(valorUnitario * quantidade * 0.009m, 2)
    };

    private static NotaFiscal NotaValida(TipoNotaFiscal tipo = TipoNotaFiscal.NFe,
        AmbienteEmissaoFiscal ambiente = AmbienteEmissaoFiscal.Homologacao) => new()
    {
        Tipo = tipo,
        Serie = "1",
        Numero = "1001",
        DataEmissao = new DateTime(2026, 8, 10, 9, 0, 0),
        NaturezaOperacaoDescricao = "VENDA DE MERCADORIA",
        Crt = "3",
        Ambiente = ambiente,
        ConsumidorFinal = true,
        DestinatarioNome = "Fulano de Tal",
        DestinatarioTipoPessoa = TipoPessoa.Fisica,
        DestinatarioDocumento = "00000000000191",
        DestinatarioIndicadorIe = IndicadorIeDestinatario.NaoContribuinte,
        DestinatarioUf = "PR",
        Itens = new List<ItemNotaFiscal> { ItemValido() },
        Pagamentos = new List<NotaFiscalPagamento>
        {
            new() { FormaPagamento = FormaPagamento.Dinheiro, Valor = 118m, Ordem = 1 }
        }
    };

    // ===== FiscalLayoutValidator: textos fixos obrigatórios em Homologação =====

    [Fact]
    public void Homologacao_DestXNome_SaiExatamenteComoOTextoExigidoPelaSefaz()
    {
        var nota = NotaValida(ambiente: AmbienteEmissaoFiscal.Homologacao);

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal(NotaFiscalPayloadBuilder.TextoDestHomologacao, payload.InfNFe.Dest?.XNome);
    }

    /// <summary>
    /// Regressão de uma rejeição real de NFC-e em Homologação: "Descricao do primeiro item
    /// diferente de NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL.
    /// [nItem:1]". Ao contrário do validador local (que só exige "contém"), a própria SEFAZ
    /// confere IGUALDADE EXATA no item 1 — por isso ele precisa ser 100% substituído pelo
    /// literal exigido, não apenas receber um sufixo mantendo a descrição real do produto.
    /// </summary>
    [Fact]
    public void Homologacao_XProdDoPrimeiroItem_SaiExatamenteComoOTextoExigidoPelaSefaz()
    {
        var nota = NotaValida(ambiente: AmbienteEmissaoFiscal.Homologacao);

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal(NotaFiscalPayloadBuilder.TextoProdHomologacaoItem1, payload.InfNFe.Det[0].Prod.XProd);
    }

    [Fact]
    public void Homologacao_XProdDoPrimeiroItem_SubstituiQualquerDescricaoJaExistente()
    {
        var nota = NotaValida(ambiente: AmbienteEmissaoFiscal.Homologacao);
        nota.Itens.First().Descricao = "NOTA FISCAL DE HOMOLOGACAO - teste";

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal(NotaFiscalPayloadBuilder.TextoProdHomologacaoItem1, payload.InfNFe.Det[0].Prod.XProd);
    }

    /// <summary>
    /// Do 2º item em diante, a regra confirmada é a mais branda do validador local (só
    /// precisa CONTER "HOMOLOGACAO") — a descrição real do produto continua visível para o
    /// operador identificar cada item na nota.
    /// </summary>
    [Fact]
    public void Homologacao_XProdDoSegundoItem_ContemHomologacaoMasPreservaADescricaoReal()
    {
        var nota = NotaValida(ambiente: AmbienteEmissaoFiscal.Homologacao);
        nota.Itens.Add(ItemValido(nItem: 2, valorUnitario: 50m, quantidade: 2m));

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        var xProdItem2 = payload.InfNFe.Det[1].Prod.XProd;
        Assert.Contains("HOMOLOGACAO", xProdItem2, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tinta Coral 18L", xProdItem2);
    }

    [Fact]
    public void Producao_DestXNomeEXProd_UsamOsDadosReaisSemTextoDeHomologacao()
    {
        var nota = NotaValida(ambiente: AmbienteEmissaoFiscal.Producao);

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal("Fulano de Tal", payload.InfNFe.Dest?.XNome);
        Assert.Equal("Tinta Coral 18L", payload.InfNFe.Det[0].Prod.XProd);
    }

    // ===== Cálculo manual: totais informados pelo operador são enviados sem recálculo =====

    [Fact]
    public void Total_CalculoManual_PreservaValoresDaNotaSemAlterarOsItens()
    {
        var nota = NotaValida();
        nota.CalculoAutomatico = false;
        nota.VProd = 150.125m;
        nota.VIpi = 7.125m;
        nota.VBcIcms = 120m;
        nota.VIcms = 12m;
        nota.VBcIcmsSt = 160m;
        nota.VIcmsSt = 3m;
        nota.VFrete = 10m;
        nota.VSeg = 5m;
        nota.VDesc = 9m;
        nota.VOutro = 4m;
        nota.VFcp = 6m;
        nota.VFcpSt = 8m;
        nota.VFcpStRet = 11m;
        nota.VIpiDevolvido = 2m;
        nota.VAproxImp = 18.125m;
        nota.VNf = 170.125m;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        var total = payload.InfNFe.Total.ICMSTot;
        Assert.Equal(150.13m, total.VProd);
        Assert.Equal(7.13m, total.VIPI);
        Assert.Equal(170.13m, total.VNF);
        Assert.Equal(18.13m, total.VTotTrib);
        Assert.Equal(new[] { 120m, 12m, 160m, 3m, 10m, 5m, 9m, 4m, 6m, 8m, 11m, 2m },
            new[] { total.VBC, total.VICMS, total.VBCST, total.VST, total.VFrete, total.VSeg,
                total.VDesc, total.VOutro, total.VFCP, total.VFCPST, total.VFCPSTRet, total.VIPIDevol });
        Assert.Equal(100m, Assert.Single(payload.InfNFe.Det).Prod.VProd);
    }

    // ===== Cálculo automático: soma dos itens precisa reconciliar com os totais =====

    [Fact]
    public void Total_VNF_ReconciliaExatamenteComASomaDosItensEnviados()
    {
        var nota = NotaValida();

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        // Mesma fórmula do MathematicalValidator real: soma por item de
        // vProd - vDesc + vFrete + vSeg + vOutro (vIS não é usado por este módulo).
        var somaItens = payload.InfNFe.Det.Sum(d =>
            d.Prod.VProd - (d.Prod.VDesc ?? 0) + (d.Prod.VFrete ?? 0) + (d.Prod.VSeg ?? 0) + (d.Prod.VOutro ?? 0));

        Assert.Equal(Math.Round(somaItens, 2), payload.InfNFe.Total.ICMSTot.VNF);
    }

    [Fact]
    public void Total_VNF_ReconciliaMesmoComFreteSeguroDescontoENotaPreCalculadaDivergente()
    {
        var nota = NotaValida();
        nota.VFrete = 10m;
        nota.VSeg = 5m;
        nota.VDesc = 3m;
        nota.VIcmsSt = 7m;
        // VNf pré-calculada deliberadamente "errada"/desatualizada — o builder não deve confiar
        // nela, e sim recalcular a partir do que realmente foi montado nos itens.
        nota.VNf = 999m;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        var somaItens = payload.InfNFe.Det.Sum(d =>
            d.Prod.VProd - (d.Prod.VDesc ?? 0) + (d.Prod.VFrete ?? 0) + (d.Prod.VSeg ?? 0) + (d.Prod.VOutro ?? 0));

        Assert.Equal(Math.Round(somaItens, 2), payload.InfNFe.Total.ICMSTot.VNF);
        Assert.NotEqual(999m, payload.InfNFe.Total.ICMSTot.VNF);

        // As acessórias (frete/seguro/desconto) foram para o primeiro item, para que a soma acima
        // bata exatamente com vNF — é assim que o MathematicalValidator real confere a nota.
        var item0 = payload.InfNFe.Det[0].Prod;
        Assert.Equal(10m, item0.VFrete);
        Assert.Equal(5m, item0.VSeg);
        Assert.Equal(3m, item0.VDesc);
        // ST não tem slot próprio por item no schema REST — embutido em vOutro para manter a
        // reconciliação. IPI tem grupo próprio agora (ver testes "Grupo IPI"), não entra aqui.
        Assert.Equal(7m, item0.VOutro);
    }

    [Fact]
    public void Total_IBSCBSTot_VIBSEVCBS_ReconciliamComASomaDosItens()
    {
        var nota = NotaValida();
        nota.Itens.Add(ItemValido(nItem: 2, valorUnitario: 50m, quantidade: 2m));
        // Totais pré-calculados deliberadamente divergentes dos itens reais.
        nota.VIbs = 1m;
        nota.VCbs = 1m;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        var somaVIbs = payload.InfNFe.Det.Sum(d => d.Imposto.IBSCBS.TribDetails.VIBS);
        var somaVCbs = payload.InfNFe.Det.Sum(d => d.Imposto.IBSCBS.TribDetails.GCBS.VCBS);

        Assert.Equal(Math.Round(somaVIbs, 2), payload.InfNFe.Total.IBSCBSTot.GIBS.VIBS);
        Assert.Equal(Math.Round(somaVCbs, 2), payload.InfNFe.Total.IBSCBSTot.GCBS.VCBS);
    }

    [Fact]
    public void Total_VProd_ReconciliaComASomaDosItens()
    {
        var nota = NotaValida();
        nota.Itens.Add(ItemValido(nItem: 2, valorUnitario: 25m, quantidade: 3m));
        nota.VProd = 1m; // deliberadamente errado

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        var somaVProd = payload.InfNFe.Det.Sum(d => d.Prod.VProd);
        Assert.Equal(Math.Round(somaVProd, 2), payload.InfNFe.Total.ICMSTot.VProd);
    }

    /// <summary>
    /// Regressão: "vProd — Soma de det[].prod.vProd de TODOS OS ITENS COM indTot='1'"
    /// (seção 4.8 do guia) — antes o total somava todos os itens sem checar indTot, mesmo
    /// que ItemNotaFiscal.CompoeTotalNota=false já fosse corretamente traduzido para
    /// prod.indTot="0" no item individual.
    /// </summary>
    [Fact]
    public void Total_VProdEVNF_IgnoraItemComIndTotZero()
    {
        var nota = NotaValida();
        var itemForaDoTotal = ItemValido(nItem: 2, valorUnitario: 40m, quantidade: 1m);
        itemForaDoTotal.CompoeTotalNota = false;
        nota.Itens.Add(itemForaDoTotal);

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal("0", payload.InfNFe.Det[1].Prod.IndTot);
        // Só o item 1 (indTot="1", vProd=100) deveria entrar — o item 2 (vProd=40) fica de fora.
        Assert.Equal(100m, payload.InfNFe.Total.ICMSTot.VProd);
        Assert.Equal(100m, payload.InfNFe.Total.ICMSTot.VNF);
    }

    // ===== Grupo IPI =====

    [Fact]
    public void Item_SemCstIpi_NaoEnviaGrupoIpi()
    {
        var nota = NotaValida(); // ItemValido() não seta CstIpi

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Null(payload.InfNFe.Det[0].Imposto.IPI);
    }

    [Fact]
    public void Item_CstIpiTributado_EnviaGrupoIpiCompleto()
    {
        var nota = NotaValida();
        var item = nota.Itens.First();
        item.CstIpi = "50";
        item.CodigoEnquadramentoIpi = "999";
        item.BaseIpi = 100m;
        item.AliquotaIpi = 5m;
        item.ValorIpi = 5m;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        var ipi = payload.InfNFe.Det[0].Imposto.IPI;
        Assert.NotNull(ipi);
        Assert.Equal("999", ipi!.CEnq);
        Assert.Equal("50", ipi.IPIDetails.CST);
        Assert.Equal(100m, ipi.IPIDetails.VBC);
        Assert.Equal(5m, ipi.IPIDetails.PIPI);
        Assert.Equal(5m, ipi.IPIDetails.VIPI);
    }

    [Fact]
    public void Item_CstIpiNaoTributado_EnviaSoOCst()
    {
        var nota = NotaValida();
        var item = nota.Itens.First();
        item.CstIpi = "53"; // saída não-tributada
        item.CodigoEnquadramentoIpi = "999";

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        var ipi = payload.InfNFe.Det[0].Imposto.IPI;
        Assert.NotNull(ipi);
        Assert.Equal("53", ipi!.IPIDetails.CST);
        Assert.Null(ipi.IPIDetails.VBC);
        Assert.Null(ipi.IPIDetails.PIPI);
        Assert.Null(ipi.IPIDetails.VIPI);
    }

    [Fact]
    public void Item_SemCodigoEnquadramentoIpiCadastrado_UsaDefault999()
    {
        var nota = NotaValida();
        var item = nota.Itens.First();
        item.CstIpi = "50";
        item.BaseIpi = 100m;
        item.AliquotaIpi = 5m;
        item.ValorIpi = 5m;
        item.CodigoEnquadramentoIpi = null;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal("999", payload.InfNFe.Det[0].Imposto.IPI!.CEnq);
    }

    /// <summary>Regressão: total.ICMSTot.VIPI deve reconciliar com a soma dos grupos IPI
    /// realmente montados por item (fonte real), não com nota.VIpi.</summary>
    [Fact]
    public void Total_VIPI_ReconciliaComASomaDosItens()
    {
        var nota = NotaValida();
        var item1 = nota.Itens.First();
        item1.CstIpi = "50";
        item1.BaseIpi = 100m;
        item1.AliquotaIpi = 5m;
        item1.ValorIpi = 5m;

        var item2 = ItemValido(nItem: 2, valorUnitario: 50m, quantidade: 1m);
        item2.CstIpi = "50";
        item2.BaseIpi = 50m;
        item2.AliquotaIpi = 10m;
        item2.ValorIpi = 5m;
        nota.Itens.Add(item2);

        nota.VIpi = 999m; // deliberadamente errado — o builder não deve confiar nele

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal(10m, payload.InfNFe.Total.ICMSTot.VIPI); // 5 + 5
    }

    /// <summary>
    /// Regressão: quando o IPI saiu do <c>vOutro</c> do item 1 e ganhou grupo próprio, ele
    /// sumiu do <c>vNF</c> — que passou a ser só a soma dos itens. O total transmitido ficava
    /// menor que a soma dos pagamentos (que sempre incluiu o IPI) e a nota era rejeitada por
    /// divergência. vNF = Σ(itens) + vIPI, como manda a fórmula oficial.
    /// </summary>
    [Fact]
    public void Total_VNF_IncluiOVIPIDosItens()
    {
        var nota = NotaValida();
        var item = nota.Itens.First();
        item.CstIpi = "50";
        item.BaseIpi = 100m;
        item.AliquotaIpi = 5m;
        item.ValorIpi = 5m;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal(105m, payload.InfNFe.Total.ICMSTot.VNF); // 100 do produto + 5 de IPI
        Assert.Equal(5m, payload.InfNFe.Total.ICMSTot.VIPI);
    }

    // ===== Grupo ICMS: campos exigidos pelo CST =====

    /// <summary>Regressão: o grupo ICMS era montado só quando <c>vBC &gt; 0</c>. Um item de
    /// valor zero (brinde/bonificação) com CST 00 saía sem vBC/pICMS/vICMS e a SEFAZ rejeitava
    /// o grupo ICMS00 por incompleto — a condição é o CST, não o valor.</summary>
    [Fact]
    public void Icms_ItemDeValorZeroComCst00_MantemGrupoCompleto()
    {
        var nota = NotaValida();
        var item = nota.Itens.First();
        item.ValorUnitario = 0m;
        item.ValorTotal = 0m;
        item.BaseIcms = 0m;
        item.ValorIcms = 0m;
        nota.Pagamentos = new List<NotaFiscalPagamento>
        {
            new() { FormaPagamento = FormaPagamento.SemPagamento, Valor = 0m, Ordem = 1 }
        };

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());
        var icms = payload.InfNFe.Det[0].Imposto.ICMS.ICMSDetails;

        Assert.Equal("3", icms.ModBC);
        Assert.Equal(0m, icms.VBC);
        Assert.Equal(18m, icms.PICMS);
        Assert.Equal(0m, icms.VICMS);
    }

    /// <summary>CST 20 (redução de base) exige pRedBC no grupo ICMS20 — o campo existia no
    /// contrato mas nunca era preenchido, então toda nota com CST 20 saía incompleta.</summary>
    [Fact]
    public void Icms_Cst20_EnviaPRedBC()
    {
        var nota = NotaValida();
        var item = nota.Itens.First();
        item.CstIcms = "20";
        item.ReducaoBaseCalculo = 40m;
        item.BaseIcms = 60m;
        item.ValorIcms = 10.80m;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());
        var icms = payload.InfNFe.Det[0].Imposto.ICMS.ICMSDetails;

        Assert.Equal(40m, icms.PRedBC);
        Assert.Equal(60m, icms.VBC);
    }

    /// <summary>CST 20 sem percentual cadastrado ainda manda pRedBC (zero) — omitir o campo
    /// derruba a nota por XSD; mandar 0,00 é válido e representa "sem redução".</summary>
    [Fact]
    public void Icms_Cst20SemReducaoCadastrada_EnviaPRedBCZerado()
    {
        var nota = NotaValida();
        nota.Itens.First().CstIcms = "20";

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal(0m, payload.InfNFe.Det[0].Imposto.ICMS.ICMSDetails.PRedBC);
    }

    /// <summary>CST 00 não reduz base — pRedBC fica omitido para não poluir o grupo com um
    /// campo que o leiaute não prevê ali.</summary>
    [Fact]
    public void Icms_Cst00_NaoEnviaPRedBC()
    {
        var payload = NotaFiscalPayloadBuilder.Construir(NotaValida(), EmitenteValido());

        Assert.Null(payload.InfNFe.Det[0].Imposto.ICMS.ICMSDetails.PRedBC);
    }

    // ===== ICMS-ST "para frente" (CST 10) e retido (CST 60) — regressão real: nota
    // rejeitada pela SEFAZ com "Nao informada vBCSTRet, pST e vICMSSTRet". =====

    /// <summary>CST 10 tem grupo próprio (vBC/pICMS/vICMS, igual CST 00) MAIS o grupo ST
    /// (vBCST/pMVAST/pICMSST/vICMSST) — os dois precisam sair juntos no mesmo item.</summary>
    [Fact]
    public void Icms_Cst10_EnviaGrupoProprioEGrupoStJuntos()
    {
        var nota = NotaValida();
        var item = nota.Itens.First();
        item.CstIcms = "10";
        item.AliquotaIcms = 18m;
        item.BaseIcms = 100m;
        item.ValorIcms = 18m;
        item.Mva = 40m;
        item.BaseIcmsSt = 140m;
        item.AliquotaIcmsSt = 18m;
        item.ValorIcmsSt = 7.20m;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());
        var icms = payload.InfNFe.Det[0].Imposto.ICMS.ICMSDetails;

        Assert.Equal("3", icms.ModBC);
        Assert.Equal(100m, icms.VBC);
        Assert.Equal(18m, icms.PICMS);
        Assert.Equal(18m, icms.VICMS);
        Assert.Equal("4", icms.ModBCST);
        Assert.Equal(40m, icms.PMVAST);
        Assert.Equal(140m, icms.VBCST);
        Assert.Equal(18m, icms.PICMSST);
        Assert.Equal(7.20m, icms.VICMSST);
    }

    /// <summary>CST 60 (ICMS-ST já retido por um elo anterior da cadeia) usa um grupo
    /// DISTINTO do CST 10: vBCSTRet/pST/vICMSSTRet — nunca vBCST/pICMSST/vICMSST. Regressão
    /// exata da rejeição real: "Nao informada vBCSTRet, pST e vICMSSTRet".</summary>
    [Fact]
    public void Icms_Cst60_EnviaGrupoRetidoDistintoDoGrupoParaFrente()
    {
        var nota = NotaValida();
        var item = nota.Itens.First();
        item.CstIcms = "60";
        item.AliquotaIcms = null;
        item.BaseIcms = null;
        item.ValorIcms = null;
        item.BaseIcmsStRetido = 100m;
        item.AliquotaIcmsStRetido = 12m;
        item.ValorIcmsStRetido = 12m;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());
        var icms = payload.InfNFe.Det[0].Imposto.ICMS.ICMSDetails;

        Assert.Equal(100m, icms.VBCSTRet);
        Assert.Equal(12m, icms.PST);
        Assert.Equal(12m, icms.VICMSSTRet);
        // Grupo "para frente" não deve vazar aqui — são schemas XML diferentes (TICMS60 x TICMS10).
        Assert.Null(icms.VBCST);
        Assert.Null(icms.PICMSST);
        Assert.Null(icms.VICMSST);
    }

    /// <summary>CSOSN 500 (equivalente Simples Nacional do CST 60) — mesmo grupo retido.</summary>
    [Fact]
    public void Icms_Csosn500_EnviaGrupoRetido()
    {
        var nota = NotaValida();
        var item = nota.Itens.First();
        item.CstIcms = null;
        item.CsosnIcms = "500";
        item.AliquotaIcms = null;
        item.BaseIcms = null;
        item.ValorIcms = null;
        item.BaseIcmsStRetido = 100m;
        item.AliquotaIcmsStRetido = 12m;
        item.ValorIcmsStRetido = 12m;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());
        var icms = payload.InfNFe.Det[0].Imposto.ICMS.ICMSDetails;

        Assert.Null(icms.CST);
        Assert.Equal("500", icms.CSOSN);
        Assert.Equal(100m, icms.VBCSTRet);
        Assert.Equal(12m, icms.PST);
        Assert.Equal(12m, icms.VICMSSTRet);
    }

    /// <summary>Regressão: PISAliq/COFINSAliq exigem vBC junto de pPIS/vPIS — o item passou a
    /// carregar as bases (antes sempre nulas) e elas precisam chegar ao payload.</summary>
    [Fact]
    public void PisCofins_BasesDoItem_ChegamAoPayload()
    {
        var nota = NotaValida();
        var item = nota.Itens.First();
        item.BasePis = 100m;
        item.AliquotaPis = 1.65m;
        item.ValorPis = 1.65m;
        item.BaseCofins = 100m;
        item.AliquotaCofins = 7.6m;
        item.ValorCofins = 7.6m;

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());
        var imposto = payload.InfNFe.Det[0].Imposto;

        Assert.Equal(100m, imposto.PIS.PISDetails.VBC);
        Assert.Equal(1.65m, imposto.PIS.PISDetails.PPIS);
        Assert.Equal(100m, imposto.COFINS.COFINSDetails.VBC);
        Assert.Equal(7.6m, imposto.COFINS.COFINSDetails.PCOFINS);
    }

    // ===== Pagamento: indPag (regressão) =====

    [Fact]
    public void Pagamento_ParceladoSaiComIndPagUm()
    {
        var nota = NotaValida();
        nota.Pagamentos = new List<NotaFiscalPagamento>
        {
            new() { FormaPagamento = FormaPagamento.CartaoCredito, Valor = 100m, QuantidadeParcelas = 3, Ordem = 1 }
        };

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal("1", payload.InfNFe.Pag.DetPag[0].IndPag);
    }

    [Fact]
    public void Pagamento_AVistaSaiComIndPagZero()
    {
        var nota = NotaValida();
        nota.Pagamentos = new List<NotaFiscalPagamento>
        {
            new() { FormaPagamento = FormaPagamento.Dinheiro, Valor = 118m, QuantidadeParcelas = 1, Ordem = 1 }
        };

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal("0", payload.InfNFe.Pag.DetPag[0].IndPag);
    }

    /// <summary>Regressão: "Sem Pagamento" mapeia para tPag="90" e omite vPag (nullable de
    /// propósito) — "Esta tag poderá ser omitida quando a tag tPag=90 (Sem Pagamento)"
    /// (leiauteNFe_v4.00.xsd, elemento vPag).</summary>
    [Fact]
    public void Pagamento_SemPagamento_SaiComTPag90EVPagOmitido()
    {
        var nota = NotaValida();
        nota.Pagamentos = new List<NotaFiscalPagamento>
        {
            new() { FormaPagamento = FormaPagamento.SemPagamento, Valor = 0m, Ordem = 1 }
        };

        var payload = NotaFiscalPayloadBuilder.Construir(nota, EmitenteValido());

        Assert.Equal("90", payload.InfNFe.Pag.DetPag[0].TPag);
        Assert.Null(payload.InfNFe.Pag.DetPag[0].VPag);
    }

    // ===== IE "ISENTO" (regressão) =====

    [Fact]
    public void Emit_IE_IsentoPreservaOTextoLiteral()
    {
        var nota = NotaValida();
        var emitente = EmitenteValido();
        emitente.InscricaoEstadual = "ISENTO";

        var payload = NotaFiscalPayloadBuilder.Construir(nota, emitente);

        Assert.Equal("ISENTO", payload.InfNFe.Emit.IE);
    }
}
