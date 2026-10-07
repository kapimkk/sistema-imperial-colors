namespace ImperialColors.Domain.Enums;

/// <summary>
/// Por onde a venda entrou na loja. Existe para os relatórios separarem faturamento por
/// canal — a mesma tinta vendida no balcão e no site conta junto no total, mas precisa
/// aparecer apartada para o lojista saber o que cada frente rende.
///
/// <b>O canal é deduzido, nunca digitado.</b> Ele sai de onde a venda foi registrada, e não
/// de um campo que alguém preenche: venda do PDV é balcão, venda do módulo de venda externa
/// é rua, e venda que o ImperialSync trouxe de um pedido pago no site é site. Um campo
/// editável permitiria marcar como site uma venda de balcão, e nenhum relatório teria como
/// perceber.
///
/// O detalhe que torna isso necessário: a venda do site <b>é gravada na mesma tabela</b> das
/// vendas de balcão. Quem as distingue é o registro da integração
/// (<c>integration.imperial_sync_operations</c>), não a tabela de vendas.
///
/// Quando a aba de Vendas Marketplace existir, os canais dela entram aqui como valores
/// novos — por isso os números são explícitos, para um valor novo nunca remontar o
/// histórico já classificado.
/// </summary>
public enum CanalVenda
{
    /// <summary>Balcão — venda registrada pelo PDV e que não veio do site.</summary>
    LojaFisica = 1,

    /// <summary>Venda externa feita na rua, registrada no módulo de vendas externas.</summary>
    VendaExternaRua = 2,

    /// <summary>Pedido pago no site, trazido para o banco da loja pelo ImperialSync.</summary>
    Site = 3
}
