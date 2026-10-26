using System.Globalization;
using ImperialColors.Application.DTOs;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Helpers;
using ImperialColors.Infrastructure.Fiscal.Contracts;

namespace ImperialColors.Infrastructure.Fiscal;

/// <summary>
/// Monta o payload JSON de emissão (<see cref="EmissaoNotaRequest"/>) a partir da
/// <see cref="NotaFiscal"/> já validada + seus itens/pagamentos + dados do emitente —
/// espelhando as seções 4 (NF-e) e 5 (NFC-e) do GUIA_INTEGRACAO.md. Cobre o caso comum de
/// cada grupo de imposto (CST/CSOSN mais usados, tabela das seções 4.7); situações raras
/// (ST com diferimento encadeado, IPI por enquadramento) usam os valores já calculados no
/// item com defaults de mercado (ModBC=3, ModBCST=4) quando a base correspondente existir.
/// Campos monetários/quantidade são <c>decimal</c> nos contratos (não <c>string</c>) — ver
/// nota em <see cref="Contracts.ProdContract"/>.
/// </summary>
internal static class NotaFiscalPayloadBuilder
{
    /// <summary>Texto exigido literalmente em <c>dest.xNome</c> quando <c>tpAmb=2</c>
    /// (Homologação) — conferido em <c>Fiscal.Shared.Services.ValidationPipeline
    /// .FiscalLayoutValidator</c> (regra <c>HOMOLOG_DEST_NOME</c>), não só no guia.</summary>
    internal const string TextoDestHomologacao = "NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    /// <summary>Sufixo aplicado ao <c>xProd</c> do 2º item em diante, em Homologação — o
    /// validador local da API (regra <c>HOMOLOG_XPROD</c>, ver <c>FiscalLayoutValidator</c>)
    /// só exige que a descrição CONTENHA "HOMOLOGACAO"/"HOMOLOGAÇÃO", não que seja
    /// substituída; por isso a descrição real do produto é preservada e só recebe o sufixo
    /// quando ainda não contém o termo.</summary>
    private const string SufixoProdHomologacao = " - NOTA FISCAL DE HOMOLOGACAO";

    /// <summary>Texto exigido literalmente (igualdade exata, não apenas "contém") no
    /// <c>xProd</c> do PRIMEIRO item em Homologação — regra da própria SEFAZ (não do
    /// validador local, que é mais permissivo), confirmada ao vivo por uma rejeição real de
    /// NFC-e: "Descricao do primeiro item diferente de ... [nItem:1]" quando o item 1 trazia
    /// a descrição real do produto com sufixo em vez deste literal exato.</summary>
    internal const string TextoProdHomologacaoItem1 = "NOTA FISCAL EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL";

    public static EmissaoNotaRequest Construir(NotaFiscal nota, EmitenteFiscalDto emitente, string? codigoNumerico = null)
    {
        var mod = ((int)nota.Tipo).ToString(CultureInfo.InvariantCulture);
        var homologacao = nota.Ambiente == AmbienteEmissaoFiscal.Homologacao;

        var dest = ConstruirDest(nota, homologacao);
        var idDest = DeterminarIdDest(emitente.Uf, nota, dest is not null);

        var itens = nota.Itens.OrderBy(i => i.NItem).ToList();
        var det = new List<DetContract>(itens.Count);
        for (var indice = 0; indice < itens.Count; indice++)
        {
            // As despesas acessórias (frete/seguro/desconto/outro) e ST/IPI só existem em campos
            // de nível NOTA no domínio (não há rateio por item) — vão inteiras no primeiro item.
            // Isso é o que permite total.ICMSTot.vNF reconciliar exatamente com a soma dos itens
            // (ver ConstruirTotal), que é como o MathematicalValidator real da API confere a nota
            // (soma por item, não uma fórmula agregada em cima dos totais já prontos).
            var ehPrimeiroItem = indice == 0;
            det.Add(ConstruirItem(itens[indice], homologacao, ehPrimeiroItem ? nota : null));
        }

        return new EmissaoNotaRequest
        {
            InfNFe = new InfNFeContract
            {
                Versao = "4.00",
                Ide = new IdeContract
                {
                    NatOp = string.IsNullOrWhiteSpace(nota.NaturezaOperacaoDescricao)
                        ? "VENDA DE MERCADORIA" : nota.NaturezaOperacaoDescricao,
                    Mod = mod,
                    Serie = nota.Serie,
                    NNF = nota.Numero,
                    DhEmi = FormatarDataHora(nota.DataEmissao),
                    TpNF = "1", // Este módulo só emite notas de Saída (vendas).
                    IdDest = idDest,
                    CMunFG = emitente.CodigoMunicipioIbge,
                    TpImp = nota.Tipo == TipoNotaFiscal.NFCe ? "4" : "1",
                    TpEmis = "1", // Emissão online síncrona — contingência SVC fora do escopo.
                    TpAmb = ((int)nota.Ambiente).ToString(CultureInfo.InvariantCulture),
                    FinNFe = ((int)nota.Finalidade).ToString(CultureInfo.InvariantCulture),
                    IndFinal = nota.ConsumidorFinal ? "1" : "0",
                    IndPres = ((int)nota.IndicadorPresenca).ToString(CultureInfo.InvariantCulture),
                    ProcEmi = "0",
                    VerProc = "ImperialColors/1.0",
                    // Gerado pelo chamador (ChaveAcessoNfeHelper) e não mais deixado em branco —
                    // sem isso a API sorteava o cNF a cada chamada e a chave de acesso mudava a
                    // cada tentativa para o MESMO nNF, causando duplicidade (cStat 539) em
                    // reenvios. Ver NotaFiscalService.EmitirAsync.
                    CNF = codigoNumerico
                },
                Emit = new EmitContract
                {
                    CNPJ = SomenteDigitos(emitente.Cnpj),
                    XNome = emitente.RazaoSocial,
                    XFant = string.IsNullOrWhiteSpace(emitente.NomeFantasia) ? null : emitente.NomeFantasia,
                    EnderEmit = new EnderecoContract
                    {
                        XLgr = emitente.Logradouro,
                        Nro = string.IsNullOrWhiteSpace(emitente.Numero) ? "S/N" : emitente.Numero,
                        XCpl = emitente.Complemento,
                        XBairro = emitente.Bairro,
                        CMun = emitente.CodigoMunicipioIbge,
                        XMun = emitente.Municipio,
                        UF = emitente.Uf,
                        CEP = SomenteDigitos(emitente.Cep)
                    },
                    IE = FormatarInscricaoEstadual(emitente.InscricaoEstadual),
                    CRT = nota.Crt
                },
                Dest = dest,
                Det = det,
                Total = ConstruirTotal(nota, det),
                Transp = new TranspContract { ModFrete = ((int)nota.FormaEnvio).ToString(CultureInfo.InvariantCulture) },
                Pag = new PagContract
                {
                    DetPag = nota.Pagamentos.OrderBy(p => p.Ordem).Select(p => new DetPagContract
                    {
                        // "0"=à vista, "1"=à prazo (layout NFe) — antes vinha sempre "0", mesmo
                        // quando NotaFiscalPagamento.QuantidadeParcelas indicava um pagamento
                        // parcelado; o dado fiscal declarado ficava inconsistente com o real.
                        IndPag = p.QuantidadeParcelas > 1 ? "1" : "0",
                        TPag = FormaPagamentoFiscalMapper.ParaTPag(p.FormaPagamento),
                        // vPag pode ser omitido só quando tPag=90 (Sem Pagamento) — qualquer
                        // outra forma sempre leva o valor real.
                        VPag = p.FormaPagamento == FormaPagamento.SemPagamento ? null : ArredondarValor(p.Valor)
                    }).ToList()
                }
            }
        };
    }

    private static DestContract? ConstruirDest(NotaFiscal nota, bool homologacao)
    {
        // NFC-e de venda sem CPF/CNPJ do comprador: dest é omitido (seção 5.2 do guia).
        if (string.IsNullOrWhiteSpace(nota.DestinatarioDocumento) && string.IsNullOrWhiteSpace(nota.DestinatarioNome))
            return null;

        var documento = SomenteDigitos(nota.DestinatarioDocumento);
        var ehJuridica = nota.DestinatarioTipoPessoa == TipoPessoa.Juridica || documento.Length == 14;

        // Em Homologação a SEFAZ (via FiscalLayoutValidator, regra HOMOLOG_DEST_NOME) exige que
        // dest.xNome seja EXATAMENTE este texto — o nome real do destinatário é ignorado de
        // propósito, senão toda emissão de teste seria barrada como "Rejeição Local".
        var xNome = homologacao
            ? TextoDestHomologacao
            : (string.IsNullOrWhiteSpace(nota.DestinatarioNome) ? "CONSUMIDOR NAO IDENTIFICADO" : nota.DestinatarioNome);

        var dest = new DestContract
        {
            XNome = xNome,
            IndIEDest = nota.DestinatarioIndicadorIe.HasValue
                ? ((int)nota.DestinatarioIndicadorIe.Value).ToString(CultureInfo.InvariantCulture)
                : "9",
            IE = nota.DestinatarioIndicadorIe == IndicadorIeDestinatario.ContribuinteIcms
                ? SomenteDigitos(nota.DestinatarioInscricaoEstadual)
                : null,
            Email = string.IsNullOrWhiteSpace(nota.DestinatarioEmail) ? null : nota.DestinatarioEmail
        };

        if (ehJuridica) dest.CNPJ = documento;
        else dest.CPF = documento;

        if (!string.IsNullOrWhiteSpace(nota.DestinatarioLogradouro))
        {
            dest.EnderDest = new EnderecoContract
            {
                XLgr = nota.DestinatarioLogradouro ?? string.Empty,
                Nro = string.IsNullOrWhiteSpace(nota.DestinatarioNumero) ? "S/N" : nota.DestinatarioNumero!,
                XCpl = nota.DestinatarioComplemento,
                XBairro = nota.DestinatarioBairro ?? string.Empty,
                CMun = nota.DestinatarioCodigoMunicipioIbge ?? string.Empty,
                XMun = nota.DestinatarioCidade ?? string.Empty,
                UF = nota.DestinatarioUf ?? string.Empty,
                CEP = SomenteDigitos(nota.DestinatarioCep)
            };
        }

        return dest;
    }

    private static string DeterminarIdDest(string ufEmitente, NotaFiscal nota, bool temDest)
    {
        if (!temDest || string.IsNullOrWhiteSpace(nota.DestinatarioUf))
            return "1";

        return string.Equals(ufEmitente, nota.DestinatarioUf, StringComparison.OrdinalIgnoreCase) ? "1" : "2";
    }

    /// <param name="notaParaAcessorias">Não nulo apenas no primeiro item da nota — carrega
    /// frete/seguro/desconto/outro (+ ST/IPI, sem campo próprio no item) para dentro de
    /// <c>prod.vFrete/vSeg/vDesc/vOutro</c> deste item específico. Ver <see cref="ConstruirTotal"/>.</param>
    private static DetContract ConstruirItem(ItemNotaFiscal item, bool homologacao, NotaFiscal? notaParaAcessorias)
    {
        var ehPrimeiroItem = notaParaAcessorias is not null;
        var xProd = item.Descricao;
        if (homologacao && ehPrimeiroItem)
        {
            // SEFAZ (não o validador local, que só exige "contém") checa igualdade exata no
            // item 1 — mandar a descrição real do produto (mesmo com sufixo) é rejeitado.
            xProd = TextoProdHomologacaoItem1;
        }
        else if (homologacao && !xProd.Contains("HOMOLOGACAO", StringComparison.OrdinalIgnoreCase) &&
            !xProd.Contains("HOMOLOGAÇÃO", StringComparison.OrdinalIgnoreCase))
        {
            xProd += SufixoProdHomologacao;
        }

        // O "Código de Barras" cadastrado no produto nem sempre é um GTIN de verdade (às
        // vezes é código interno, PLU de balança etc.) — mandar isso cru em cEAN/cEANTrib
        // é o que causa a rejeição "GTIN inválido" da SEFAZ. GTIN não é obrigatório: o
        // literal "SEM GTIN" é aceito (seção 4.2 do guia), então só usamos o código
        // cadastrado quando ele passa na conferência de dígito verificador do GtinHelper.
        var gtin = GtinHelper.EhValido(item.CodigoBarras) ? item.CodigoBarras! : "SEM GTIN";

        var prod = new ProdContract
        {
            CProd = item.CodigoProduto,
            CEAN = gtin,
            XProd = xProd,
            NCM = item.Ncm ?? string.Empty,
            CEST = item.Cest,
            CFOP = item.Cfop,
            UCom = item.Unidade,
            QCom = ArredondarQuantidade(item.Quantidade),
            VUnCom = ArredondarValorUnitario(item.ValorUnitario),
            VProd = ArredondarValor(item.ValorTotal),
            CEANTrib = gtin,
            UTrib = item.UnidadeTributavel ?? item.Unidade,
            QTrib = ArredondarQuantidade(item.QuantidadeTributavel ?? item.Quantidade),
            VUnTrib = ArredondarValorUnitario(item.ValorUnitarioTributavel ?? item.ValorUnitario),
            IndTot = item.CompoeTotalNota ? "1" : "0"
        };

        if (notaParaAcessorias is { } nota)
        {
            prod.VFrete = ValorSeDiferenteDeZero(nota.VFrete);
            prod.VSeg = ValorSeDiferenteDeZero(nota.VSeg);
            prod.VDesc = ValorSeDiferenteDeZero(nota.VDesc);
            // VST não tem slot próprio no item (não existe no schema REST por item) — a única
            // forma de manter total.ICMSTot.vNF reconciliado com a soma dos itens (ver
            // ConstruirTotal) é embutir esse valor em vOutro, junto do vOutro real da nota.
            // VIPI NÃO entra mais aqui — agora vai no grupo IPI próprio de cada item
            // (ConstruirIpi), e ConstruirTotal soma o VIPI a partir desse grupo.
            var outro = nota.VOutro + nota.VIcmsSt;
            prod.VOutro = ValorSeDiferenteDeZero(outro);
        }

        return new DetContract
        {
            NItem = item.NItem.ToString(CultureInfo.InvariantCulture),
            Prod = prod,
            Imposto = new ImpostoContract
            {
                ICMS = new IcmsWrapperContract { ICMSDetails = ConstruirIcms(item) },
                IPI = ConstruirIpi(item),
                PIS = new PisWrapperContract
                {
                    PISDetails = new PisDetailsContract
                    {
                        CST = item.CstPis ?? "07",
                        VBC = ArredondarValorOpcional(item.BasePis),
                        PPIS = ArredondarPercentualOpcional(item.AliquotaPis),
                        VPIS = ArredondarValorOpcional(item.ValorPis)
                    }
                },
                COFINS = new CofinsWrapperContract
                {
                    COFINSDetails = new CofinsDetailsContract
                    {
                        CST = item.CstCofins ?? "07",
                        VBC = ArredondarValorOpcional(item.BaseCofins),
                        PCOFINS = ArredondarPercentualOpcional(item.AliquotaCofins),
                        VCOFINS = ArredondarValorOpcional(item.ValorCofins)
                    }
                },
                IBSCBS = new IbsCbsContract
                {
                    CST = item.CstIbsCbs ?? "000",
                    CClassTrib = item.CClassTrib ?? "000001",
                    TribDetails = new TribDetailsContract
                    {
                        VBC = ArredondarValor(item.BaseIbsCbs ?? item.ValorTotal),
                        GIBSUF = new GIbsUfItemContract
                        {
                            PIBSUF = ArredondarPercentual(item.AliquotaIbsUf),
                            VIBSUF = ArredondarValor(item.ValorIbsUf)
                        },
                        GIBSMun = new GIbsMunItemContract
                        {
                            PIBSMun = ArredondarPercentual(item.AliquotaIbsMunicipio),
                            VIBSMun = ArredondarValor(item.ValorIbsMunicipio)
                        },
                        VIBS = ArredondarValor((item.ValorIbsUf ?? 0) + (item.ValorIbsMunicipio ?? 0)),
                        GCBS = new GCbsItemContract
                        {
                            PCBS = ArredondarPercentual(item.AliquotaCbs),
                            VCBS = ArredondarValor(item.ValorCbs)
                        }
                    }
                }
            }
        };
    }

    /// <summary>CST de IPI que não geram vIPI/vBC/pIPI no grupo — só o CST é enviado. Mesma
    /// classificação usada pela API real para decidir entre TIpiTrib e TIpiNT
    /// (Fiscal.Shared.Services.JsonToXsdResolverService.DeserializeIpi).</summary>
    private static readonly HashSet<string> CstIpiNaoTributado =
        ["01", "02", "03", "04", "05", "51", "52", "53", "54", "55"];

    /// <summary>Grupo IPI é opcional — null (chave "IPI" omitida do JSON) quando o item não
    /// tem CST de IPI cadastrado, que é o caso da maioria dos produtos de uma loja de
    /// varejo. Antes, quando havia valor de IPI, ele ia disfarçado em prod.vOutro, sem
    /// CST/enquadramento/alíquota declarados — uma lacuna de conformidade fiscal.</summary>
    private static IpiWrapperContract? ConstruirIpi(ItemNotaFiscal item)
    {
        if (string.IsNullOrWhiteSpace(item.CstIpi))
            return null;

        var tributado = !CstIpiNaoTributado.Contains(item.CstIpi);

        return new IpiWrapperContract
        {
            CEnq = string.IsNullOrWhiteSpace(item.CodigoEnquadramentoIpi) ? "999" : item.CodigoEnquadramentoIpi,
            IPIDetails = new IpiDetailsContract
            {
                CST = item.CstIpi,
                VBC = tributado ? (decimal?)ArredondarValor(item.BaseIpi ?? item.ValorTotal) : null,
                PIPI = tributado ? ArredondarPercentualOpcional(item.AliquotaIpi) : null,
                VIPI = tributado ? (decimal?)ArredondarValor(item.ValorIpi) : null
            }
        };
    }

    /// <summary>CST cujo grupo no leiaute exige <c>modBC</c>/<c>vBC</c>/<c>pICMS</c>/
    /// <c>vICMS</c> — CST 00/20/51/90 (tributação integral) mais o CST 10 (Substituição
    /// Tributária "para frente", que tem grupo próprio ADEMAIS do grupo ST — ver seção 4.7
    /// do guia: "10 | ... | Grupo 00 + modBCST/pMVAST/vBCST/pICMSST/vICMSST"). Espelha
    /// <c>CalculoFiscalHelper.CstIcmsTributacaoIntegral</c>/<c>CstIcmsStParaFrente</c> (que
    /// são internal da camada Application, daí a cópia, mesma abordagem já usada em
    /// <see cref="CstIpiNaoTributado"/>).</summary>
    private static readonly HashSet<string> CstIcmsComDestaque = ["00", "20", "51", "90", "10"];

    /// <summary>CST que reduzem base e por isso exigem <c>pRedBC</c> no grupo, mesmo que o
    /// percentual seja zero — omitir derruba a nota por XSD incompleto.</summary>
    private static readonly HashSet<string> CstIcmsExigeRedBC = ["20", "70"];

    private static IcmsDetailsContract ConstruirIcms(ItemNotaFiscal item)
    {
        var cst = item.CstIcms ?? string.Empty;

        var details = new IcmsDetailsContract
        {
            Orig = item.Origem ?? "0",
            CST = string.IsNullOrWhiteSpace(item.CstIcms) ? null : item.CstIcms,
            CSOSN = string.IsNullOrWhiteSpace(item.CsosnIcms) ? null : item.CsosnIcms
        };

        // A condição é o CST, não o valor da base: um item de valor zero (brinde, bonificação)
        // com CST 00 saía sem vBC/pICMS/vICMS e era rejeitado por grupo ICMS00 incompleto —
        // o `BaseIcms is > 0` continua no OU só para não perder itens legados que trazem base
        // com um CST fora da lista.
        if (CstIcmsComDestaque.Contains(cst) || item.BaseIcms is > 0)
        {
            details.ModBC = "3";
            details.VBC = ArredondarValor(item.BaseIcms);
            details.PICMS = ArredondarPercentual(item.AliquotaIcms);
            details.VICMS = ArredondarValor(item.ValorIcms);

            if (CstIcmsExigeRedBC.Contains(cst) || item.ReducaoBaseCalculo is > 0)
                details.PRedBC = ArredondarPercentual(item.ReducaoBaseCalculo);
        }

        // ICMS-ST "para frente" (CST 10 / CSOSN 201/202/203) — grupo vBCST/pMVAST/pICMSST/
        // vICMSST. BaseIcmsSt só sai preenchido pelo CalculoFiscalHelper quando MVA e
        // AliquotaIcmsSt estão cadastrados (NotaFiscalValidator bloqueia a emissão antes
        // disso, então na prática todo item que chega aqui com esse CST/CSOSN tem os dados).
        if (item.BaseIcmsSt is > 0)
        {
            details.ModBCST = "4";
            details.PMVAST = ArredondarPercentualOpcional(item.Mva);
            details.VBCST = ArredondarValor(item.BaseIcmsSt);
            details.PICMSST = ArredondarPercentual(item.AliquotaIcmsSt);
            details.VICMSST = ArredondarValor(item.ValorIcmsSt);
        }

        // ICMS-ST retido anteriormente (CST 60 / CSOSN 500) — grupo vBCSTRet/pST/vICMSSTRet,
        // DISTINTO do grupo "para frente" acima (schema TICMS60/TICMSSN500, não TICMS10).
        // Regressão real corrigida aqui: "Nao informada vBCSTRet, pST e vICMSSTRet".
        if (item.BaseIcmsStRetido is > 0)
        {
            details.VBCSTRet = ArredondarValor(item.BaseIcmsStRetido);
            details.PST = ArredondarPercentual(item.AliquotaIcmsStRetido);
            details.VICMSSTRet = ArredondarValor(item.ValorIcmsStRetido);
        }

        return details;
    }

    /// <summary>
    /// Os totais que a API confere matematicamente (<c>Fiscal.Shared.Services.ValidationPipeline
    /// .MathematicalValidator</c>) são recalculados aqui a partir dos itens que REALMENTE vão no
    /// payload (<paramref name="det"/>), em vez de usar os campos pré-calculados de
    /// <see cref="NotaFiscal"/> (<c>nota.VNf</c>/<c>VIbs</c>/<c>VCbs</c>) direto — o validador real
    /// soma <c>vProd - vDesc + vFrete + vSeg + vOutro</c> de cada item e compara com <c>total
    /// .ICMSTot.vNF</c> (regra <c>SOMA_TOTAIS_DIVERGENTE</c>), e separadamente soma <c>vIBS</c>/
    /// <c>vCBS</c> de cada item contra <c>total.IBSCBSTot.gIBS.vIBS</c>/<c>gCBS.vCBS</c> (regras
    /// <c>SOMA_IBS_DIVERGENTE</c>/<c>SOMA_CBS_DIVERGENTE</c>) — uma fórmula mais simples do que a
    /// documentada na seção 4.8 do guia (que inclui vST/vIPI/vICMSDeson agregados). Qualquer
    /// diferença de arredondamento entre o valor pré-calculado em <see cref="NotaFiscal"/> e a soma
    /// real dos itens enviados vira "Rejeição Local: Inconsistências detectadas" — daí recalcular
    /// aqui, na fonte da verdade que é o que efetivamente foi montado em <paramref name="det"/>.
    /// </summary>
    private static TotalContract ConstruirTotal(NotaFiscal nota, IReadOnlyList<DetContract> det)
    {
        // "vProd — Soma de det[].prod.vProd de TODOS OS ITENS COM indTot='1'" (seção 4.8 do
        // guia) — sem esse filtro, um item marcado como "não compõe total"
        // (ItemNotaFiscal.CompoeTotalNota, já traduzido para indTot pelo ProdContract) ainda
        // entrava na soma, divergindo da semântica documentada do campo.
        var itensQueCompoem = det.Where(d => d.Prod.IndTot == "1").ToList();
        var vProdSoma = itensQueCompoem.Sum(d => d.Prod.VProd);
        var vIbsSoma = det.Sum(d => d.Imposto.IBSCBS.TribDetails.VIBS);
        var vCbsSoma = det.Sum(d => d.Imposto.IBSCBS.TribDetails.GCBS.VCBS);
        // No cálculo automático, o IPI acompanha o grupo próprio de cada item.
        // O modo manual preserva os totais da nota, sem alterar os impostos dos itens.
        var vIpiSoma = det.Sum(d => d.Imposto.IPI?.IPIDetails.VIPI ?? 0);

        // vNF = Σ(vProd − vDesc + vFrete + vSeg + vOutro) + vIPI. O vIPI entra por FORA da soma
        // por item porque não existe campo de IPI em det[].prod — quando o IPI deixou o vOutro
        // do item 1 e passou a ter grupo próprio, ele sumiu do total: o vNF transmitido ficava
        // menor que a soma dos pagamentos (pag.vPag, que sempre incluiu o IPI via nota.VNf) e a
        // nota era rejeitada por total divergente. Bate com a fórmula oficial da seção 4.8 do
        // guia e com NotaFiscalService.RecalcularTotais.
        var vNfSoma = itensQueCompoem.Sum(d => d.Prod.VProd - (d.Prod.VDesc ?? 0) + (d.Prod.VFrete ?? 0) + (d.Prod.VSeg ?? 0) + (d.Prod.VOutro ?? 0))
            + vIpiSoma;

        return new TotalContract
        {
            ICMSTot = new IcmsTotContract
            {
                VBC = ArredondarValor(nota.VBcIcms),
                VICMS = ArredondarValor(nota.VIcms),
                VICMSDeson = 0m,
                VFCP = ArredondarValor(nota.VFcp),
                VBCST = ArredondarValor(nota.VBcIcmsSt),
                VST = ArredondarValor(nota.VIcmsSt),
                VFCPST = ArredondarValor(nota.VFcpSt),
                VFCPSTRet = ArredondarValor(nota.VFcpStRet),
                VProd = nota.CalculoAutomatico ? ArredondarValor(vProdSoma) : ArredondarValor(nota.VProd),
                VFrete = ArredondarValor(nota.VFrete),
                VSeg = ArredondarValor(nota.VSeg),
                VDesc = ArredondarValor(nota.VDesc),
                VII = 0m,
                VIPI = nota.CalculoAutomatico ? ArredondarValor(vIpiSoma) : ArredondarValor(nota.VIpi),
                VIPIDevol = ArredondarValor(nota.VIpiDevolvido),
                VPIS = Math.Round(nota.Itens.Sum(i => i.ValorPis ?? 0), 2, MidpointRounding.AwayFromZero),
                VCOFINS = Math.Round(nota.Itens.Sum(i => i.ValorCofins ?? 0), 2, MidpointRounding.AwayFromZero),
                VOutro = ArredondarValor(nota.VOutro),
                VNF = nota.CalculoAutomatico ? ArredondarValor(vNfSoma) : ArredondarValor(nota.VNf),
                VTotTrib = nota.VAproxImp == 0m ? null : ArredondarValor(nota.VAproxImp)
            },
            IBSCBSTot = new IbsCbsTotContract
            {
                VBCIBSCBS = ArredondarValor(nota.VBcIbsCbs),
                GIBS = new GIbsTotContract
                {
                    GIBSUF = new GDifDevTribUfContract { VIBSUF = ArredondarValor(nota.VIbsUf) },
                    GIBSMun = new GDifDevTribMunContract { VIBSMun = ArredondarValor(nota.VIbsMunicipio) },
                    VIBS = Math.Round(vIbsSoma, 2, MidpointRounding.AwayFromZero)
                },
                GCBS = new GCbsTotContract { VCBS = Math.Round(vCbsSoma, 2, MidpointRounding.AwayFromZero) }
            }
        };
    }

    private static string SomenteDigitos(string? valor) =>
        new((valor ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>"ISENTO" é um valor literal aceito pelo schema para empresas sem IE — precisa
    /// ser preservado como texto; qualquer outro valor é normalizado para dígitos apenas.
    /// (<see cref="SomenteDigitos"/> sozinho zeraria "ISENTO" para uma string vazia.)</summary>
    private static string FormatarInscricaoEstadual(string? valor)
    {
        var normalizado = (valor ?? string.Empty).Trim();
        return normalizado.Equals("ISENTO", StringComparison.OrdinalIgnoreCase)
            ? "ISENTO"
            : SomenteDigitos(normalizado);
    }

    // Os campos monetários/percentuais/quantidade do payload são `decimal` nos contratos (não
    // `string`) — o modelo real da API (Fiscal.Shared.Models) declara esses campos como decimal
    // puro, sem JsonNumberHandling.AllowReadingFromString configurado do lado do servidor. Estes
    // helpers só arredondam para a escala documentada no guia (2 casas para valores/percentuais,
    // 4 para quantidade/valor unitário) — a serialização do decimal como número JSON é feita
    // pelo System.Text.Json preservando essa escala exata (ex.: 100.0000m -> 100.0000, sem aspas).
    private static decimal ArredondarValor(decimal? valor) => Math.Round(valor ?? 0m, 2, MidpointRounding.AwayFromZero);
    private static decimal? ArredondarValorOpcional(decimal? valor) => valor.HasValue ? ArredondarValor(valor) : null;
    private static decimal ArredondarValorUnitario(decimal valor) => Math.Round(valor, 4, MidpointRounding.AwayFromZero);
    private static decimal ArredondarQuantidade(decimal valor) => Math.Round(valor, 4, MidpointRounding.AwayFromZero);
    private static decimal ArredondarPercentual(decimal? valor) => Math.Round(valor ?? 0m, 2, MidpointRounding.AwayFromZero);
    private static decimal? ArredondarPercentualOpcional(decimal? valor) => valor.HasValue ? ArredondarPercentual(valor) : null;

    /// <summary>Campos opcionais do item (vFrete/vSeg/vDesc/vOutro) ficam omitidos quando zero —
    /// evita poluir o payload com "0.00" em todo item que não é o primeiro.</summary>
    private static decimal? ValorSeDiferenteDeZero(decimal valor)
    {
        var arredondado = ArredondarValor(valor);
        return arredondado == 0m ? null : arredondado;
    }

    /// <summary>ISO-8601 com offset local (ex.: 2026-08-05T08:00:00-03:00) — a API rejeita "Z"/UTC.</summary>
    private static string FormatarDataHora(DateTime dataHora)
    {
        var semKind = DateTime.SpecifyKind(dataHora, DateTimeKind.Unspecified);
        var offset = TimeZoneInfo.Local.GetUtcOffset(semKind);
        return new DateTimeOffset(semKind, offset).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }
}
