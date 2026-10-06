using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Domain.ReadModels;

namespace ImperialColors.Application.Services;

public class VendaSiteService : IVendaSiteService
{
    public const int MaximoItensPorPagina = 200;
    public const int MaximoCaracteresBusca = 100;

    private readonly IVendaSiteRepository _repository;

    public VendaSiteService(IVendaSiteRepository repository)
    {
        _repository = repository;
    }

    public async Task<ResultadoVendasSiteDto> ObterPaginadoAsync(
        int pagina, int itensPorPagina, string? termoBusca = null, CancellationToken cancellationToken = default)
    {
        pagina = Math.Max(1, pagina);
        itensPorPagina = Math.Clamp(itensPorPagina, 1, MaximoItensPorPagina);

        var resultado = await _repository.ObterPaginadoAsync(
            pagina, itensPorPagina, NormalizarBusca(termoBusca), cancellationToken);

        return new ResultadoVendasSiteDto
        {
            Situacao = resultado.Situacao,
            Pagina = new PaginacaoResultadoDto<VendaSiteDto>
            {
                Itens = resultado.Itens.Select(Mapear).ToList(),
                PaginaAtual = pagina,
                ItensPorPagina = itensPorPagina,
                TotalItens = resultado.Total
            }
        };
    }

    /// <summary>Busca em branco vira "sem busca"; o resto é aparado e limitado, para um texto
    /// colado por engano não virar um padrão gigante no banco.</summary>
    public static string? NormalizarBusca(string? termoBusca)
    {
        if (string.IsNullOrWhiteSpace(termoBusca))
            return null;

        var termo = termoBusca.Trim();
        return termo.Length > MaximoCaracteresBusca ? termo[..MaximoCaracteresBusca] : termo;
    }

    public static VendaSiteDto Mapear(LinhaVendaSite linha) => new()
    {
        OperacaoId = linha.OperacaoId,
        PedidoSite = linha.PedidoSite,
        VendaId = linha.VendaId,
        NumeroVenda = linha.NumeroVenda,
        VendaExiste = linha.VendaExiste,
        Cliente = linha.VendaExiste
            ? (string.IsNullOrWhiteSpace(linha.Cliente) ? "Consumidor Final" : linha.Cliente!)
            : "—",
        DataVenda = linha.DataVenda,
        Total = linha.Total,
        Pagamento = DescreverPagamento(linha),
        Parcelas = linha.VendaExiste ? linha.Parcelas : null,
        StatusDescricao = DescreverStatus(linha),
        SincronizadoEm = linha.SincronizadoEm
    };

    private static string DescreverPagamento(LinhaVendaSite linha)
    {
        if (!linha.VendaExiste || linha.FormaPagamento is null)
            return "—";

        return linha.QuantidadePagamentos > 1
            ? "Pagamento Misto"
            : PagamentoHelper.ObterDescricao(linha.FormaPagamento.Value, linha.Parcelas ?? 1);
    }

    private static string DescreverStatus(LinhaVendaSite linha)
    {
        if (!linha.VendaExiste)
            return "Venda removida";

        if (!linha.VendaAtiva)
            return "Excluída";

        return linha.Status switch
        {
            StatusVenda.Aberta => "Aberta",
            StatusVenda.Finalizada => "Finalizada",
            StatusVenda.Cancelada => "Cancelada",
            _ => "Desconhecido"
        };
    }
}
