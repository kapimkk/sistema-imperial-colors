using ImperialColors.Domain.Enums;

namespace ImperialColors.Application.DTOs;

/// <summary>
/// Dados fiscais de um produto. Usado tanto para leitura quanto para salvar
/// (upsert) a tributação — se nunca foi preenchida, todos os campos vêm nulos.
/// </summary>
public class TributacaoProdutoDto
{
    public int ProdutoId { get; set; }
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
    public decimal? ValorIpiFixo { get; set; }
    public string? ExTipi { get; set; }

    public string? UnidadeTributavel { get; set; }
    public decimal? FatorConversao { get; set; }
    public string? GtinTributavel { get; set; }

    /// <summary>CFOP para venda dentro do estado do emitente (normalmente 5xxx).</summary>
    public string? CfopDentroEstado { get; set; }

    /// <summary>CFOP para venda para outro estado (normalmente 6xxx).</summary>
    public string? CfopForaEstado { get; set; }

    /// <summary>CST do IBS/CBS (Reforma Tributária — LC 214/2025) — 3 dígitos.</summary>
    public string? CstIbsCbs { get; set; }

    /// <summary>Código de Classificação Tributária do IBS/CBS — 6 dígitos (CST + sequencial).</summary>
    public string? CClassTrib { get; set; }

    /// <summary>CST do Imposto Seletivo — 3 dígitos (normalmente vazio fora do "IS").</summary>
    public string? CstIS { get; set; }

    /// <summary>cClassTrib do Imposto Seletivo — 6 dígitos.</summary>
    public string? CClassTribIS { get; set; }
    public decimal? AliquotaIS { get; set; }

    /// <summary>Exceção à alíquota principal do IBS Municipal (Regra Geral da empresa) — só
    /// preencha quando este produto tem tratamento diferente.</summary>
    public decimal? AliquotaIbsMunicipioDiferimento { get; set; }
    public decimal? AliquotaIbsMunicipioReducao { get; set; }

    /// <summary>True quando qualquer campo fiscal está preenchido, inclusive percentuais
    /// iguais a zero. Usado para salvar e reabrir a seção sem descartar campos isolados.</summary>
    public bool Preenchida =>
        !string.IsNullOrWhiteSpace(Ncm) || !string.IsNullOrWhiteSpace(Cest) || Origem.HasValue ||
        !string.IsNullOrWhiteSpace(CstIcms) ||
        !string.IsNullOrWhiteSpace(CsosnIcms) || !string.IsNullOrWhiteSpace(CstIbsCbs) ||
        !string.IsNullOrWhiteSpace(CstPis) || !string.IsNullOrWhiteSpace(CstCofins) ||
        !string.IsNullOrWhiteSpace(CstIpi) || !string.IsNullOrWhiteSpace(CfopDentroEstado) ||
        !string.IsNullOrWhiteSpace(CfopForaEstado) ||
        !string.IsNullOrWhiteSpace(CodigoEnquadramentoIpi) || !string.IsNullOrWhiteSpace(ExTipi) ||
        !string.IsNullOrWhiteSpace(UnidadeTributavel) || !string.IsNullOrWhiteSpace(GtinTributavel) ||
        !string.IsNullOrWhiteSpace(CClassTrib) || !string.IsNullOrWhiteSpace(CstIS) ||
        !string.IsNullOrWhiteSpace(CClassTribIS) ||
        AliquotaIcms.HasValue || AliquotaIcmsSt.HasValue || Mva.HasValue || ReducaoBaseCalculo.HasValue ||
        AliquotaIcmsStRetido.HasValue || AliquotaPis.HasValue || AliquotaCofins.HasValue ||
        AliquotaIpi.HasValue || ValorIpiFixo.HasValue || FatorConversao.HasValue || AliquotaIS.HasValue ||
        AliquotaIbsMunicipioDiferimento.HasValue || AliquotaIbsMunicipioReducao.HasValue;
}
