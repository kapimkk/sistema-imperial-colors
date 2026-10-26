using ImperialColors.Domain.Enums;

namespace ImperialColors.Application.DTOs;

/// <summary>
/// Perfil fiscal padrão de uma categoria — usado para pré-preencher a tributação de
/// produtos novos cadastrados nela. Mesmos campos de <see cref="TributacaoProdutoDto"/>,
/// exceto GTIN/unidade tributável/fator de conversão/IPI fixo/EX TIPI (específicos de
/// cada produto — código de barras único, embalagem, tabela TIPI por item).
/// </summary>
public class TributacaoCategoriaDto
{
    public int CategoriaId { get; set; }
    public string? Ncm { get; set; }
    public string? Cest { get; set; }
    public OrigemMercadoria? Origem { get; set; }

    public string? CstIcms { get; set; }
    public string? CsosnIcms { get; set; }
    public decimal? AliquotaIcms { get; set; }
    public decimal? AliquotaIcmsSt { get; set; }
    public decimal? Mva { get; set; }
    public decimal? ReducaoBaseCalculo { get; set; }

    /// <summary>Percentual do ICMS-ST retido (pST) — só para CST 60 / CSOSN 500.</summary>
    public decimal? AliquotaIcmsStRetido { get; set; }

    public string? CstPis { get; set; }
    public decimal? AliquotaPis { get; set; }

    public string? CstCofins { get; set; }
    public decimal? AliquotaCofins { get; set; }

    public string? CstIpi { get; set; }
    public string? CodigoEnquadramentoIpi { get; set; }
    public decimal? AliquotaIpi { get; set; }

    /// <summary>CFOP para venda dentro do estado do emitente (normalmente 5xxx).</summary>
    public string? CfopDentroEstado { get; set; }

    /// <summary>CFOP para venda para outro estado (normalmente 6xxx).</summary>
    public string? CfopForaEstado { get; set; }

    /// <summary>CST do IBS/CBS (Reforma Tributária — LC 214/2025) — 3 dígitos.</summary>
    public string? CstIbsCbs { get; set; }

    /// <summary>Código de Classificação Tributária do IBS/CBS — 6 dígitos.</summary>
    public string? CClassTrib { get; set; }

    /// <summary>CST do Imposto Seletivo — 3 dígitos.</summary>
    public string? CstIS { get; set; }

    /// <summary>cClassTrib do Imposto Seletivo — 6 dígitos.</summary>
    public string? CClassTribIS { get; set; }
    public decimal? AliquotaIS { get; set; }

    public decimal? AliquotaIbsMunicipioDiferimento { get; set; }
    public decimal? AliquotaIbsMunicipioReducao { get; set; }

    /// <summary>Mesma regra de <see cref="TributacaoProdutoDto.Preenchida"/> para os campos
    /// compartilhados: um padrão com apenas um campo também deve ser herdado pelo produto.</summary>
    public bool Preenchida => ParaProdutoDto(produtoId: 0).Preenchida;

    /// <summary>Converte para o DTO de produto (usado ao herdar o padrão na tela de cadastro).</summary>
    public TributacaoProdutoDto ParaProdutoDto(int produtoId) => new()
    {
        ProdutoId = produtoId,
        Ncm = Ncm,
        Cest = Cest,
        Origem = Origem,
        CstIcms = CstIcms,
        CsosnIcms = CsosnIcms,
        AliquotaIcms = AliquotaIcms,
        AliquotaIcmsSt = AliquotaIcmsSt,
        Mva = Mva,
        ReducaoBaseCalculo = ReducaoBaseCalculo,
        AliquotaIcmsStRetido = AliquotaIcmsStRetido,
        CstPis = CstPis,
        AliquotaPis = AliquotaPis,
        CstCofins = CstCofins,
        AliquotaCofins = AliquotaCofins,
        CstIpi = CstIpi,
        CodigoEnquadramentoIpi = CodigoEnquadramentoIpi,
        AliquotaIpi = AliquotaIpi,
        CfopDentroEstado = CfopDentroEstado,
        CfopForaEstado = CfopForaEstado,
        CstIbsCbs = CstIbsCbs,
        CClassTrib = CClassTrib,
        CstIS = CstIS,
        CClassTribIS = CClassTribIS,
        AliquotaIS = AliquotaIS,
        AliquotaIbsMunicipioDiferimento = AliquotaIbsMunicipioDiferimento,
        AliquotaIbsMunicipioReducao = AliquotaIbsMunicipioReducao
    };

    /// <summary>Constrói o perfil da categoria a partir dos campos preenchidos num produto.</summary>
    public static TributacaoCategoriaDto DoProdutoDto(int categoriaId, TributacaoProdutoDto produto) => new()
    {
        CategoriaId = categoriaId,
        Ncm = produto.Ncm,
        Cest = produto.Cest,
        Origem = produto.Origem,
        CstIcms = produto.CstIcms,
        CsosnIcms = produto.CsosnIcms,
        AliquotaIcms = produto.AliquotaIcms,
        AliquotaIcmsSt = produto.AliquotaIcmsSt,
        Mva = produto.Mva,
        ReducaoBaseCalculo = produto.ReducaoBaseCalculo,
        AliquotaIcmsStRetido = produto.AliquotaIcmsStRetido,
        CstPis = produto.CstPis,
        AliquotaPis = produto.AliquotaPis,
        CstCofins = produto.CstCofins,
        AliquotaCofins = produto.AliquotaCofins,
        CstIpi = produto.CstIpi,
        CodigoEnquadramentoIpi = produto.CodigoEnquadramentoIpi,
        AliquotaIpi = produto.AliquotaIpi,
        CfopDentroEstado = produto.CfopDentroEstado,
        CfopForaEstado = produto.CfopForaEstado,
        CstIbsCbs = produto.CstIbsCbs,
        CClassTrib = produto.CClassTrib,
        CstIS = produto.CstIS,
        CClassTribIS = produto.CClassTribIS,
        AliquotaIS = produto.AliquotaIS,
        AliquotaIbsMunicipioDiferimento = produto.AliquotaIbsMunicipioDiferimento,
        AliquotaIbsMunicipioReducao = produto.AliquotaIbsMunicipioReducao
    };
}
