using ImperialColors.Application.Services;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Domain.ReadModels;
using Moq;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// "Vendas Site": o serviço só normaliza a consulta e traduz as linhas do banco para o que a tela
/// mostra. A lista em si nasce do registro da integração (vide <c>VendaSiteIntegrationTests</c>).
/// </summary>
public class VendaSiteServiceTests
{
    private static readonly DateTime Agora = new(2026, 10, 6, 16, 45, 56);

    private readonly Mock<IVendaSiteRepository> _repositorio = new();
    private readonly VendaSiteService _servico;

    public VendaSiteServiceTests()
    {
        _repositorio
            .Setup(r => r.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginaVendasSite());
        _servico = new VendaSiteService(_repositorio.Object);
    }

    private static LinhaVendaSite Linha(Action<LinhaVendaSite>? ajustar = null)
    {
        var linha = new LinhaVendaSite
        {
            OperacaoId = Guid.NewGuid(),
            PedidoSite = "IC-2026-000001",
            VendaId = 27,
            NumeroVenda = "20261006-0001",
            VendaExiste = true,
            VendaAtiva = true,
            Status = StatusVenda.Finalizada,
            Cliente = "Cliente Teste Pix",
            DataVenda = Agora,
            Total = 365.47m,
            FormaPagamento = FormaPagamento.Pix,
            Parcelas = 1,
            QuantidadePagamentos = 1,
            SincronizadoEm = Agora
        };
        ajustar?.Invoke(linha);
        return linha;
    }

    private async Task<Application.DTOs.VendaSiteDto> Mapear(Action<LinhaVendaSite>? ajustar = null)
    {
        _repositorio
            .Setup(r => r.ObterPaginadoAsync(1, 50, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginaVendasSite { Itens = [Linha(ajustar)], Total = 1 });

        var resultado = await _servico.ObterPaginadoAsync(1, 50);

        return Assert.Single(resultado.Pagina.Itens);
    }

    // ---------------------------------------------------------------------------------------
    // Consulta
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    public async Task Pagina_NuncaMenorQueUm(int pedida, int esperada)
    {
        await _servico.ObterPaginadoAsync(pedida, 50);

        _repositorio.Verify(r => r.ObterPaginadoAsync(esperada, 50, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(50, 50)]
    [InlineData(200, 200)]
    [InlineData(100000, 200)]
    public async Task ItensPorPagina_FicamEntreUmEDuzentos(int pedido, int esperado)
    {
        await _servico.ObterPaginadoAsync(1, pedido);

        _repositorio.Verify(r => r.ObterPaginadoAsync(1, esperado, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("IC-2026-000001", "IC-2026-000001")]
    [InlineData("  maria  ", "maria")]
    public async Task Busca_EmBrancoViraSemBusca_ERestoEhAparado(string? digitado, string? esperado)
    {
        await _servico.ObterPaginadoAsync(1, 50, digitado);

        _repositorio.Verify(r => r.ObterPaginadoAsync(1, 50, esperado, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Busca_MuitoLonga_EhLimitada()
    {
        await _servico.ObterPaginadoAsync(1, 50, new string('a', 500));

        _repositorio.Verify(
            r => r.ObterPaginadoAsync(1, 50, new string('a', VendaSiteService.MaximoCaracteresBusca), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TotalDePaginas_VemDoTotalEDoTamanhoDaPagina()
    {
        _repositorio
            .Setup(r => r.ObterPaginadoAsync(2, 50, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginaVendasSite { Itens = [Linha()], Total = 101 });

        var resultado = await _servico.ObterPaginadoAsync(2, 50);

        Assert.Equal(2, resultado.Pagina.PaginaAtual);
        Assert.Equal(50, resultado.Pagina.ItensPorPagina);
        Assert.Equal(101, resultado.Pagina.TotalItens);
        Assert.Equal(3, resultado.Pagina.TotalPaginas);
    }

    [Theory]
    [InlineData(SituacaoIntegracaoSite.Disponivel)]
    [InlineData(SituacaoIntegracaoSite.NaoInstalada)]
    [InlineData(SituacaoIntegracaoSite.SemPermissao)]
    public async Task SituacaoDaIntegracao_ChegaATela(SituacaoIntegracaoSite situacao)
    {
        _repositorio
            .Setup(r => r.ObterPaginadoAsync(1, 50, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginaVendasSite { Situacao = situacao });

        var resultado = await _servico.ObterPaginadoAsync(1, 50);

        Assert.Equal(situacao, resultado.Situacao);
        Assert.Empty(resultado.Pagina.Itens);
    }

    [Fact]
    public async Task ErroDoRepositorio_Sobe()
    {
        _repositorio
            .Setup(r => r.ObterPaginadoAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falha"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _servico.ObterPaginadoAsync(1, 50));
    }

    // ---------------------------------------------------------------------------------------
    // Tradução da linha
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task VendaPix_MostraTudoQueOOperadorPrecisaConferir()
    {
        var dto = await Mapear();

        Assert.Equal("IC-2026-000001", dto.PedidoSite);
        Assert.Equal("20261006-0001", dto.NumeroVenda);
        Assert.Equal("Cliente Teste Pix", dto.Cliente);
        Assert.Equal("Pix", dto.Pagamento);
        Assert.Equal("1x", dto.ParcelasDescricao);
        Assert.Equal("Finalizada", dto.StatusDescricao);
        Assert.Equal("06/10/2026 16:45", dto.DataVendaDescricao);
        Assert.Equal("06/10/2026 16:45", dto.SincronizadoEmDescricao);
        Assert.Equal(365.47m, dto.Total);
        Assert.Contains("365,47", dto.TotalDescricao);
        Assert.True(dto.VendaExiste);
    }

    [Fact]
    public async Task CartaoEmTresVezes_MostraAsParcelas()
    {
        var dto = await Mapear(l =>
        {
            l.FormaPagamento = FormaPagamento.CartaoCredito;
            l.Parcelas = 3;
        });

        Assert.Equal("Cartão de Crédito - 3x", dto.Pagamento);
        Assert.Equal("3x", dto.ParcelasDescricao);
    }

    [Fact]
    public async Task VariosPagamentos_ViraPagamentoMisto()
    {
        var dto = await Mapear(l => l.QuantidadePagamentos = 2);

        Assert.Equal("Pagamento Misto", dto.Pagamento);
    }

    [Theory]
    [InlineData(StatusVenda.Finalizada, "Finalizada")]
    [InlineData(StatusVenda.Cancelada, "Cancelada")]
    [InlineData(StatusVenda.Aberta, "Aberta")]
    public async Task StatusDaVenda_ViraTexto(StatusVenda status, string esperado)
    {
        var dto = await Mapear(l => l.Status = status);

        Assert.Equal(esperado, dto.StatusDescricao);
    }

    [Fact]
    public async Task VendaInativada_MostraExcluida()
    {
        var dto = await Mapear(l => l.VendaAtiva = false);

        Assert.Equal("Excluída", dto.StatusDescricao);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SemNomeDeComprador_MostraConsumidorFinal(string? nome)
    {
        var dto = await Mapear(l => l.Cliente = nome);

        Assert.Equal("Consumidor Final", dto.Cliente);
    }

    [Fact]
    public async Task VendaApagadaDepoisDeSincronizada_ContinuaNaListaComoRemovida()
    {
        // O registro da integração não tem chave estrangeira para a venda: se alguém a excluiu no
        // sistema, a linha sobrevive só com o que o registro guardou.
        var dto = await Mapear(l =>
        {
            l.VendaExiste = false;
            l.VendaAtiva = false;
            l.Status = null;
            l.Cliente = null;
            l.DataVenda = null;
            l.Total = null;
            l.FormaPagamento = null;
            l.Parcelas = null;
            l.QuantidadePagamentos = 0;
        });

        Assert.Equal("Venda removida", dto.StatusDescricao);
        Assert.Equal("IC-2026-000001", dto.PedidoSite);
        Assert.Equal("20261006-0001", dto.NumeroVenda);
        Assert.Equal("—", dto.Cliente);
        Assert.Equal("—", dto.Pagamento);
        Assert.Equal("—", dto.ParcelasDescricao);
        Assert.Equal("—", dto.TotalDescricao);
        Assert.Equal("—", dto.DataVendaDescricao);
        Assert.False(dto.VendaExiste);
        Assert.Equal("06/10/2026 16:45", dto.SincronizadoEmDescricao);
    }
}
