using ImperialColors.Domain.Enums;

namespace ImperialColors.Application.DTOs;

/// <summary>
/// Nota fiscal (NF-e/NFC-e) — espelha <c>NotaFiscal</c> quase 1:1, usado tanto para
/// cadastro/edição do rascunho (tela "Cadastrar NF") quanto para exibir o resultado da
/// emissão. Campos grandes (XML autorizado, caminho do arquivo local) ficam fora —
/// acessados sob demanda via <c>INotaFiscalService.ObterXmlAsync</c>.
/// </summary>
public class NotaFiscalDto
{
    public int Id { get; set; }
    public TipoNotaFiscal Tipo { get; set; } = TipoNotaFiscal.NFe;
    public int? VendaId { get; set; }
    public int? ClienteId { get; set; }
    public string? ClienteNome { get; set; }
    public int? NaturezaOperacaoId { get; set; }
    public string? TipoSaida { get; set; } = "Emissão própria";
    public string Serie { get; set; } = "1";
    public string Numero { get; set; } = string.Empty;
    public DateTime DataEmissao { get; set; } = DateTime.Now;
    public DateTime? DataSaida { get; set; }
    public string NaturezaOperacaoDescricao { get; set; } = string.Empty;
    public FinalidadeNfe Finalidade { get; set; } = FinalidadeNfe.Normal;
    public bool ConsumidorFinal { get; set; } = true;
    public IndicadorPresencaComprador IndicadorPresenca { get; set; } = IndicadorPresencaComprador.Presencial;
    public string? IntermediadorCnpj { get; set; }
    public string? IntermediadorIdentificador { get; set; }
    public string Crt { get; set; } = string.Empty;
    public AmbienteEmissaoFiscal Ambiente { get; set; } = AmbienteEmissaoFiscal.Homologacao;

    // Destinatário
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

    // Cálculo do imposto
    /// <summary>Quando falso, os totais informados no cadastro são preservados na emissão.</summary>
    public bool CalculoAutomatico { get; set; } = true;
    public decimal VProd { get; set; }
    public decimal VServ { get; set; }
    public decimal VFrete { get; set; }
    public decimal VSeg { get; set; }
    public decimal VBcIcms { get; set; }
    public decimal VIcms { get; set; }
    public decimal VBcIcmsSt { get; set; }
    public decimal VIcmsSt { get; set; }
    public decimal VIpi { get; set; }
    public decimal VIpiDevolvido { get; set; }
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

    // Transportador / Volumes
    public ModalidadeFrete FormaEnvio { get; set; } = ModalidadeFrete.SemOcorrenciaTransporte;
    public decimal? PesoBruto { get; set; }
    public decimal? PesoLiquido { get; set; }
    public bool EnviarParaExpedicao { get; set; }

    // Pagamento
    public string? FormaRecebimento { get; set; }
    public string? CategoriaFinanceira { get; set; }
    public string? CondicaoPagamento { get; set; }

    // Dados adicionais
    public string? Deposito { get; set; }
    public string? Observacoes { get; set; }
    public string? ObservacoesSistema { get; set; }
    public string? InformacoesFisco { get; set; }
    public string? Marcadores { get; set; }

    // Resultado da emissão
    public StatusNotaFiscal Status { get; set; } = StatusNotaFiscal.Rascunho;
    public string? ChaveAcesso { get; set; }
    public string? NProt { get; set; }
    public DateTime? DhRecbto { get; set; }
    public string? CStat { get; set; }
    public string? XMotivo { get; set; }
    public string? QrCodeUrl { get; set; }
    public string? MensagemErro { get; set; }

    public List<ItemNotaFiscalDto> Itens { get; set; } = new();
    public List<NotaFiscalPagamentoDto> Pagamentos { get; set; } = new();
    public List<NotaFiscalEventoDto> Eventos { get; set; } = new();

    public bool PodeEditar => Status is StatusNotaFiscal.Rascunho or StatusNotaFiscal.Rejeitada;
    public bool PodeCancelar => Status == StatusNotaFiscal.Autorizada;
}

public class ItemNotaFiscalDto
{
    public int Id { get; set; }
    public int? ProdutoId { get; set; }
    public int NItem { get; set; }
    public string CodigoProduto { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public string? Ncm { get; set; }
    public string? Cest { get; set; }
    public string Cfop { get; set; } = string.Empty;
    public string Unidade { get; set; } = "UN";
    public decimal Quantidade { get; set; } = 1;
    public decimal ValorUnitario { get; set; }
    public decimal ValorTotal { get; set; }
    public string? UnidadeTributavel { get; set; }
    public decimal? QuantidadeTributavel { get; set; }
    public decimal? ValorUnitarioTributavel { get; set; }
    public bool CompoeTotalNota { get; set; } = true;

    public string? Origem { get; set; }
    public string? CstIcms { get; set; }
    public string? CsosnIcms { get; set; }

    /// <summary>CST ou CSOSN — o que estiver preenchido. Só para exibição: a grade de itens
    /// da nota não mostrava nenhum dado de ICMS, então o operador não tinha como ver (nem
    /// conferir) o código e a alíquota que estavam bloqueando a emissão.</summary>
    public string? CodigoIcms => string.IsNullOrWhiteSpace(CstIcms) ? CsosnIcms : CstIcms;
    public decimal? BaseIcms { get; set; }

    /// <summary>pRedBC — percentual de redução da base (CST 20/70). <see cref="BaseIcms"/>
    /// já vem com a redução aplicada.</summary>
    public decimal? ReducaoBaseCalculo { get; set; }
    public decimal? AliquotaIcms { get; set; }
    public decimal? ValorIcms { get; set; }
    /// <summary>pMVAST — MVA/IVA-ST usado no cálculo do ICMS-ST "para frente" (CST 10 /
    /// CSOSN 201/202/203).</summary>
    public decimal? Mva { get; set; }
    public decimal? BaseIcmsSt { get; set; }
    public decimal? AliquotaIcmsSt { get; set; }
    public decimal? ValorIcmsSt { get; set; }

    /// <summary>ICMS-ST retido anteriormente (CST 60 / CSOSN 500) — vBCSTRet/pST/vICMSSTRet,
    /// grupo distinto do "para frente" acima.</summary>
    public decimal? BaseIcmsStRetido { get; set; }
    public decimal? AliquotaIcmsStRetido { get; set; }
    public decimal? ValorIcmsStRetido { get; set; }

    public string? CstPis { get; set; }
    public decimal? BasePis { get; set; }
    public decimal? AliquotaPis { get; set; }
    public decimal? ValorPis { get; set; }
    public string? CstCofins { get; set; }
    public decimal? BaseCofins { get; set; }
    public decimal? AliquotaCofins { get; set; }
    public decimal? ValorCofins { get; set; }

    public string? CstIpi { get; set; }
    public decimal? BaseIpi { get; set; }
    public decimal? AliquotaIpi { get; set; }
    public decimal? ValorIpi { get; set; }
    public string? CodigoEnquadramentoIpi { get; set; }

    public string? CstIbsCbs { get; set; }
    public string? CClassTrib { get; set; }
    public decimal? BaseIbsCbs { get; set; }
    public decimal? AliquotaIbsUf { get; set; }
    public decimal? ValorIbsUf { get; set; }
    public decimal? AliquotaIbsMunicipio { get; set; }
    public decimal? ValorIbsMunicipio { get; set; }
    public decimal? AliquotaCbs { get; set; }
    public decimal? ValorCbs { get; set; }

    public List<string> Avisos { get; set; } = new();
}

public class NotaFiscalPagamentoDto
{
    public int Id { get; set; }
    public FormaPagamento FormaPagamento { get; set; } = FormaPagamento.Dinheiro;
    public decimal Valor { get; set; }
    public int QuantidadeParcelas { get; set; } = 1;
    public int Ordem { get; set; }
}

public class NotaFiscalEventoDto
{
    public int Id { get; set; }
    public TipoEventoNotaFiscal Tipo { get; set; }
    public DateTime DataHora { get; set; }
    public bool Sucesso { get; set; }
    public string? NProtEvento { get; set; }
    public string? CStat { get; set; }
    public string? XMotivo { get; set; }
    public string? Texto { get; set; }
    public int? Sequencial { get; set; }
    public string? Usuario { get; set; }
}

/// <summary>Linha resumida para as telas de lista (NF-e/NFC-e).</summary>
public class NotaFiscalResumoDto
{
    public int Id { get; set; }
    public TipoNotaFiscal Tipo { get; set; }
    public string Serie { get; set; } = string.Empty;
    public string Numero { get; set; } = string.Empty;
    public DateTime DataEmissao { get; set; }
    public StatusNotaFiscal Status { get; set; }
    public string? ClienteNome { get; set; }
    public decimal VNf { get; set; }
    public string? ChaveAcesso { get; set; }
}

/// <summary>Painel-resumo da tela de Nota Fiscal (hub NF-e/NFC-e) — contadores gerais
/// (NF-e + NFC-e somadas) e as últimas notas emitidas, para o operador ter uma visão geral
/// sem precisar entrar em cada lista separadamente.</summary>
public class ResumoNotasFiscaisDto
{
    public int TotalEmitidas { get; set; }
    public decimal ValorTotalEmitido { get; set; }

    public int EmitidasHoje { get; set; }
    public decimal ValorEmitidoHoje { get; set; }
    public int EmitidasNoMes { get; set; }
    public decimal ValorEmitidoNoMes { get; set; }

    public int TotalCanceladas { get; set; }
    public int TotalRejeitadas { get; set; }
    public int TotalDenegadas { get; set; }

    /// <summary>Rascunho + Indeterminada — aguardando uma decisão do operador.</summary>
    public int TotalPendentes { get; set; }

    public int TotalNFe { get; set; }
    public decimal ValorNFe { get; set; }
    public int TotalNFCe { get; set; }
    public decimal ValorNFCe { get; set; }

    /// <summary>Nulo quando ainda não há nenhuma nota autorizada — 0/0 é indefinido, não
    /// zero, e a tela precisa diferenciar "sem dado ainda" de "ticket médio zerado".</summary>
    public decimal? TicketMedio => TotalEmitidas > 0 ? ValorTotalEmitido / TotalEmitidas : null;

    public List<NotaFiscalResumoDto> UltimasNotas { get; set; } = new();
}
