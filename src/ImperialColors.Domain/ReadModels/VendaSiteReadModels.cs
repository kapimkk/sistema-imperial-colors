using ImperialColors.Domain.Enums;

namespace ImperialColors.Domain.ReadModels;

/// <summary>
/// Se a tela "Vendas Site" consegue ler o registro da integração com o site
/// (<c>integration.imperial_sync_operations</c>, criado à mão pelos scripts SQL do ImperialSync).
/// </summary>
public enum SituacaoIntegracaoSite
{
    /// <summary>O schema existe e o usuário do banco pode lê-lo.</summary>
    Disponivel = 1,

    /// <summary>O schema <c>integration</c> não existe neste banco: os scripts SQL do ImperialSync
    /// ainda não foram executados (ou o sistema aponta para outro banco).</summary>
    NaoInstalada = 2,

    /// <summary>O schema existe, mas o usuário que o sistema usa para conectar não pode lê-lo.</summary>
    SemPermissao = 3
}

/// <summary>
/// Uma venda que o ImperialSync criou neste banco a partir de um pedido pago no site: a ponte
/// "pedido do site ↔ operação ↔ venda local", lida do registro de idempotência da integração
/// e ligada à venda do próprio sistema.
///
/// A ligação com <c>vendas</c> é <b>sem chave estrangeira de propósito</b> (uma FK impediria o
/// sistema de excluir uma venda). Por isso a venda pode não existir mais: nesse caso
/// <see cref="VendaExiste"/> é falso e só o que o registro guardou (número da venda, pedido e
/// data da sincronização) é conhecido.
/// </summary>
public class LinhaVendaSite
{
    public Guid OperacaoId { get; set; }
    public string PedidoSite { get; set; } = string.Empty;
    public int VendaId { get; set; }
    public string NumeroVenda { get; set; } = string.Empty;

    public bool VendaExiste { get; set; }

    /// <summary>Falso quando a venda foi inativada (exclusão lógica) no sistema da loja.</summary>
    public bool VendaAtiva { get; set; }

    public StatusVenda? Status { get; set; }
    public string? Cliente { get; set; }
    public DateTime? DataVenda { get; set; }
    public decimal? Total { get; set; }
    public FormaPagamento? FormaPagamento { get; set; }
    public int? Parcelas { get; set; }
    public int QuantidadePagamentos { get; set; }

    /// <summary>Quando o ImperialSync gravou a venda neste banco (horário local da loja).</summary>
    public DateTime SincronizadoEm { get; set; }
}

public class PaginaVendasSite
{
    public SituacaoIntegracaoSite Situacao { get; init; } = SituacaoIntegracaoSite.Disponivel;
    public IReadOnlyList<LinhaVendaSite> Itens { get; init; } = Array.Empty<LinhaVendaSite>();
    public int Total { get; init; }
}
