using ImperialColors.Domain.Enums;

namespace ImperialColors.Domain.Entities;

/// <summary>
/// Nota fiscal (NF-e modelo 55 ou NFC-e modelo 65) emitida através da API Fiscal externa
/// (PFCode). Guarda tanto os dados que o operador preenche na tela de emissão quanto o
/// resultado devolvido pela API — a nota é documento fiscal legal, então nada aqui deve
/// depender de dados mutáveis de outras tabelas (Cliente/Produto podem mudar depois; os
/// campos relevantes ficam snapshotados nesta entidade e em <see cref="ItemNotaFiscal"/>).
/// </summary>
public class NotaFiscal : BaseEntity
{
    // --- Bloco "Nota fiscal" ---
    public TipoNotaFiscal Tipo { get; set; } = TipoNotaFiscal.NFe;
    public int? VendaId { get; set; }
    public int? ClienteId { get; set; }
    public int? NaturezaOperacaoId { get; set; }
    public string? TipoSaida { get; set; } = "Emissão própria";
    public string Serie { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public DateTime DataEmissao { get; set; } = DateTime.Now;
    public DateTime? DataSaida { get; set; }
    public string NaturezaOperacaoDescricao { get; set; } = string.Empty;
    public FinalidadeNfe Finalidade { get; set; } = FinalidadeNfe.Normal;
    public bool ConsumidorFinal { get; set; } = true;
    public IndicadorPresencaComprador IndicadorPresenca { get; set; } = IndicadorPresencaComprador.Presencial;
    public string? IntermediadorCnpj { get; set; }
    public string? IntermediadorIdentificador { get; set; }

    /// <summary>CRT do emitente no momento da emissão (snapshot de
    /// <c>ConfiguracaoFiscalService.ObterCodigoCrtAsync</c>) — decide se os itens usam
    /// CST ou CSOSN.</summary>
    public string Crt { get; set; } = string.Empty;
    public AmbienteEmissaoFiscal Ambiente { get; set; } = AmbienteEmissaoFiscal.Homologacao;

    // --- Bloco "Destinatário" (snapshot — o cadastro do Cliente pode mudar depois) ---
    public string? DestinatarioNome { get; set; }
    public TipoPessoa? DestinatarioTipoPessoa { get; set; }
    public string? DestinatarioDocumento { get; set; }
    public IndicadorIeDestinatario? DestinatarioIndicadorIe { get; set; }
    public string? DestinatarioInscricaoEstadual { get; set; }
    public string? DestinatarioEmail { get; set; }
    public string? DestinatarioTelefone { get; set; }
    public string? DestinatarioCep { get; set; }
    public string? DestinatarioLogradouro { get; set; }
    public string? DestinatarioNumero { get; set; }
    public string? DestinatarioComplemento { get; set; }
    public string? DestinatarioBairro { get; set; }
    public string? DestinatarioCidade { get; set; }
    public string? DestinatarioUf { get; set; }
    public string? DestinatarioCodigoMunicipioIbge { get; set; }
    public string? Suframa { get; set; }
    public string? Vendedor { get; set; }
    public string? ListaPrecoNome { get; set; }

    public bool EntregaDiferenteCobranca { get; set; }
    public string? EntregaCep { get; set; }
    public string? EntregaLogradouro { get; set; }
    public string? EntregaNumero { get; set; }
    public string? EntregaComplemento { get; set; }
    public string? EntregaBairro { get; set; }
    public string? EntregaCidade { get; set; }
    public string? EntregaUf { get; set; }
    public string? EntregaCodigoMunicipioIbge { get; set; }

    // --- Bloco "Cálculo do imposto" — nomes de campo seguem a tag oficial do total.ICMSTot
    // /total.IBSCBSTot, mesma convenção já usada em TotaisFiscaisVendaDto. ---
    /// <summary>Quando falso, os totais informados no cadastro são preservados na emissão.</summary>
    public bool CalculoAutomatico { get; set; } = true;
    public decimal VProd { get; set; }

    /// <summary>
    /// ⚠️ Sempre 0 — não é transmitido e não entra em nenhum cálculo de total.
    /// Serviços/ISSQN pertencem à NFS-e, que este módulo não emite: o payload de NF-e/NFC-e
    /// (modelos 55/65) não tem grupo ISSQNtot. Enquanto havia campo na tela para preencher,
    /// o valor entrava em <c>VNf</c> mas não no <c>vNF</c> do XML — a soma dos pagamentos
    /// exigida localmente ficava maior que o total transmitido e a SEFAZ rejeitava a nota.
    /// A coluna permanece no banco só para não exigir migration destrutiva; se a NFS-e for
    /// implementada um dia, o grupo ISSQN precisa ir junto ao payload, não só ao total.
    /// </summary>
    public decimal VServ { get; set; }
    public decimal VFrete { get; set; }
    public decimal VSeg { get; set; }
    public decimal VBcIcms { get; set; }
    public decimal VIcms { get; set; }
    public decimal VBcIcmsSt { get; set; }
    public decimal VIcmsSt { get; set; }
    public decimal VIpi { get; set; }
    public decimal VIpiDevolvido { get; set; }
    /// <summary>⚠️ Sempre 0 — mesmo motivo de <see cref="VServ"/>: ISSQN é da NFS-e.</summary>
    public decimal VIssqn { get; set; }
    public decimal VOutro { get; set; }
    public decimal VDesc { get; set; }
    public decimal VFunrural { get; set; }
    public int NumeroItens { get; set; }
    public decimal VAproxImp { get; set; }
    public decimal VFcp { get; set; }
    public decimal VFcpSt { get; set; }
    public decimal VFcpStRet { get; set; }
    public decimal VBcIbsCbs { get; set; }
    public decimal VIbsUf { get; set; }
    public decimal VIbsMunicipio { get; set; }
    public decimal VIbs { get; set; }
    public decimal VCbs { get; set; }
    public decimal VNf { get; set; }

    // --- Bloco "Transportador / Volumes" ---
    public ModalidadeFrete FormaEnvio { get; set; } = ModalidadeFrete.SemOcorrenciaTransporte;
    public decimal? PesoBruto { get; set; }
    public decimal? PesoLiquido { get; set; }
    public bool EnviarParaExpedicao { get; set; }

    // --- Bloco "Pagamento" ---
    public string? FormaRecebimento { get; set; }
    public string? CategoriaFinanceira { get; set; }
    public string? CondicaoPagamento { get; set; }

    // --- Bloco "Dados adicionais" ---
    public string? Deposito { get; set; }
    public string? Observacoes { get; set; }
    public string? ObservacoesSistema { get; set; }
    public string? InformacoesFisco { get; set; }
    public string? Marcadores { get; set; }

    // --- Resultado da emissão (preenchido pela API Fiscal) ---
    public StatusNotaFiscal Status { get; set; } = StatusNotaFiscal.Rascunho;
    public string? ChaveAcesso { get; set; }
    public string? NProt { get; set; }
    public DateTime? DhRecbto { get; set; }
    public string? CStat { get; set; }
    public string? XMotivo { get; set; }
    public string? XmlAutorizado { get; set; }
    public string? QrCodeUrl { get; set; }
    public string? CaminhoXmlLocal { get; set; }
    public string? TraceId { get; set; }
    public string? MensagemErro { get; set; }

    public Venda? Venda { get; set; }
    public Cliente? Cliente { get; set; }
    public NaturezaOperacao? NaturezaOperacao { get; set; }
    public ICollection<ItemNotaFiscal> Itens { get; set; } = new List<ItemNotaFiscal>();
    public ICollection<NotaFiscalPagamento> Pagamentos { get; set; } = new List<NotaFiscalPagamento>();
    public ICollection<NotaFiscalEvento> Eventos { get; set; } = new List<NotaFiscalEvento>();
}
