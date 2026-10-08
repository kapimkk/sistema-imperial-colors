using ImperialColors.Application.DTOs;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// AUDITORIA (15/09, segunda rodada) — venda, troca, cancelamento e venda externa.
/// Cada teste afirma o comportamento CORRETO esperado; os que falham são achados.
/// Nenhuma linha de produção foi alterada.
/// </summary>
public class Auditoria2VendaTrocaTests
{
    private static string Sufixo() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// CORRIGIDO (C2). Antes, a troca só comparava com a quantidade ORIGINAL do item: vendeu 2,
    /// dava para "devolver" 2 quantas vezes quisesse. Agora TrocaRepository soma o já devolvido.
    /// </summary>
    [Fact]
    public async Task TrocarOMesmoItemDuasVezes_NaoPodeDevolverMaisDoQueFoiVendido()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var produtoA = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta A {sufixo}", 100m, 10m);
        var produtoB = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta B {sufixo}", 100m, 10m);

        var venda = await infra.VenderAsync(produtoA.Id, 2m, 100m);
        var trocaService = infra.Servico<ITrocaService>();

        RegistrarTrocaDto Troca() => new()
        {
            VendaOrigemId = venda.Id,
            ItemVendaOrigemId = venda.Itens[0].Id,
            QuantidadeDevolvida = 2m,
            RetornarAoEstoque = true,
            ProdutoNovoId = produtoB.Id,
            QuantidadeNova = 2m,
            PrecoUnitarioNovo = 100m,
            Usuario = "auditoria2"
        };

        await trocaService.RegistrarAsync(Troca());

        var segunda = await Record.ExceptionAsync(() => trocaService.RegistrarAsync(Troca()));
        var estoqueA = await infra.EstoqueAsync(produtoA.Id);

        Assert.True(segunda is DomainException,
            $"a segunda troca do mesmo item (2 de 2 já devolvidos) foi aceita; estoque de A terminou em {estoqueA} " +
            "partindo de 10 com só 2 vendidos.");
        Assert.Contains("Restam 0", segunda!.Message);
        Assert.Equal(10m, estoqueA);
    }

    /// <summary>
    /// C2 sob concorrência: duas telas registrando a devolução total do mesmo item ao mesmo tempo.
    /// A trava por venda dentro da transação garante que só uma passe.
    /// </summary>
    [Fact]
    public async Task DuasTrocasSimultaneasDoMesmoItem_SoUmaPodeSerAceita()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var produtoA = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta A {sufixo}", 100m, 10m);
        var produtoB = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta B {sufixo}", 100m, 10m);
        var venda = await infra.VenderAsync(produtoA.Id, 2m, 100m);

        async Task<Exception?> TrocarAsync()
        {
            await using var scope = infra.Provider.CreateAsyncScope();
            return await Record.ExceptionAsync(() => scope.ServiceProvider.GetRequiredService<ITrocaService>().RegistrarAsync(new RegistrarTrocaDto
            {
                VendaOrigemId = venda.Id,
                ItemVendaOrigemId = venda.Itens[0].Id,
                QuantidadeDevolvida = 2m,
                RetornarAoEstoque = true,
                ProdutoNovoId = produtoB.Id,
                QuantidadeNova = 1m,
                PrecoUnitarioNovo = 100m,
                Usuario = "auditoria2"
            }));
        }

        var resultados = await Task.WhenAll(TrocarAsync(), TrocarAsync(), TrocarAsync());

        Assert.Equal(1, resultados.Count(r => r is null));
        Assert.All(resultados.Where(r => r is not null), r => Assert.IsType<DomainException>(r));
        Assert.Equal(10m, await infra.EstoqueAsync(produtoA.Id));
    }

    /// <summary>
    /// CORRIGIDO (C3). Antes, o cancelamento repunha todos os itens originais, inclusive os que já
    /// tinham voltado ao estoque pela troca. Agora recusa, igual à exclusão permanente.
    /// </summary>
    [Fact]
    public async Task CancelarVendaQueJaTeveTroca_DeveSerBloqueado()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var produtoA = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta A {sufixo}", 100m, 10m);
        var produtoB = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta B {sufixo}", 100m, 10m);

        var venda = await infra.VenderAsync(produtoA.Id, 2m, 100m);

        await infra.Servico<ITrocaService>().RegistrarAsync(new RegistrarTrocaDto
        {
            VendaOrigemId = venda.Id,
            ItemVendaOrigemId = venda.Itens[0].Id,
            QuantidadeDevolvida = 2m,
            RetornarAoEstoque = true,
            ProdutoNovoId = produtoB.Id,
            QuantidadeNova = 1m,
            PrecoUnitarioNovo = 100m,
            Usuario = "auditoria2"
        });

        var erro = await Record.ExceptionAsync(() => infra.Servico<IVendaService>().CancelarAsync(venda.Id));
        var estoqueA = await infra.EstoqueAsync(produtoA.Id);
        var estoqueB = await infra.EstoqueAsync(produtoB.Id);

        Assert.True(erro is DomainException,
            $"cancelamento aceito depois da troca: estoque A = {estoqueA} (começou em 10), " +
            $"estoque B = {estoqueB} (cliente levou 1 e a venda sumiu do faturamento).");
        Assert.Equal(10m, estoqueA);
        Assert.Equal(9m, estoqueB);
    }

    /// <summary>
    /// C3, variante da venda externa: a edição não pode deixar a venda com menos unidades do
    /// que já voltou por troca (estornaria de novo). Editar sem violar isso continua permitido.
    /// </summary>
    [Fact]
    public async Task EditarVendaExternaAbaixoDoJaDevolvido_DeveSerBloqueado()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var produtoA = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta A {sufixo}", 50m, 10m);
        var produtoB = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta B {sufixo}", 50m, 10m);

        var servico = infra.Servico<IVendaExternaService>();
        var venda = await servico.RegistrarAsync(new RegistrarVendaExternaDto
        {
            Observacoes = "auditoria2",
            Usuario = "auditoria2",
            Itens = [new RegistrarItemVendaExternaDto { ProdutoId = produtoA.Id, NomeProduto = produtoA.Nome, Quantidade = 5m, PrecoBase = 50m, PrecoUnitario = 50m }]
        });
        infra.RegistrarVendaExterna(venda.Id);
        infra.RegistrarMarcadorAuditoria(venda.NumeroVendaExterna);

        await infra.Servico<ITrocaService>().RegistrarVendaExternaAsync(new RegistrarTrocaVendaExternaDto
        {
            VendaExternaOrigemId = venda.Id,
            ItemVendaExternaOrigemId = venda.Itens[0].Id,
            QuantidadeDevolvida = 2m,
            RetornarAoEstoque = true,
            ProdutoNovoId = produtoB.Id,
            QuantidadeNova = 1m,
            PrecoUnitarioNovo = 50m,
            Usuario = "auditoria2"
        });

        AtualizarVendaExternaDto Edicao(decimal quantidade) => new()
        {
            Id = venda.Id,
            Observacoes = "auditoria2",
            Usuario = "auditoria2",
            Itens = [new AtualizarItemVendaExternaDto { Id = venda.Itens[0].Id, ProdutoId = produtoA.Id, NomeProduto = produtoA.Nome, Quantidade = quantidade, PrecoBase = 50m, PrecoUnitario = 50m }]
        };

        var abaixo = await Record.ExceptionAsync(() => servico.AtualizarAsync(Edicao(1m)));
        Assert.IsType<DomainException>(abaixo);
        Assert.Equal(7m, await infra.EstoqueAsync(produtoA.Id));

        await servico.AtualizarAsync(Edicao(3m));
        Assert.Equal(9m, await infra.EstoqueAsync(produtoA.Id));
    }

    /// <summary>
    /// CORRIGIDO (A1). Antes, a venda com nota autorizada sumia do faturamento e do estoque
    /// enquanto a NF-e/NFC-e continuava válida na SEFAZ.
    /// </summary>
    [Fact]
    public async Task CancelarVendaComNotaFiscalAutorizada_DeveSerBloqueado()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var produto = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta NF {sufixo}", 50m, 10m);
        var venda = await infra.VenderAsync(produto.Id, 1m, 50m);

        await using (var ctx = await infra.ContextFactory.CreateDbContextAsync())
        {
            var nota = new NotaFiscal
            {
                Tipo = TipoNotaFiscal.NFCe,
                Serie = "987",
                Numero = Random.Shared.Next(100_000_000, 999_999_999).ToString(),
                Ambiente = AmbienteEmissaoFiscal.Homologacao,
                Status = StatusNotaFiscal.Autorizada,
                VendaId = venda.Id,
                Crt = "1",
                NaturezaOperacaoDescricao = "VENDA AUDITORIA2"
            };
            ctx.NotasFiscais.Add(nota);
            await ctx.SaveChangesAsync();
            infra.RegistrarNota(nota.Id);
        }

        var vendaService = infra.Servico<IVendaService>();
        var erro = await Record.ExceptionAsync(() => vendaService.CancelarAsync(venda.Id));
        var erroExclusao = await Record.ExceptionAsync(() => vendaService.ExcluirFisicamenteAsync(venda.Id));

        Assert.True(erro is DomainException,
            "venda com NFC-e AUTORIZADA foi cancelada sem nenhum aviso — a nota continua válida na SEFAZ.");
        Assert.Contains("NFC-e", erro!.Message);
        Assert.IsType<DomainException>(erroExclusao);
        Assert.Equal(9m, await infra.EstoqueAsync(produto.Id));
    }

    /// <summary>
    /// CORRIGIDO (A2). Antes, cancelar e excluir uma venda só chamavam ILogger (console, que num
    /// WinExe não vai para lugar nenhum) e nada chegava a logs_auditoria.
    /// </summary>
    [Fact]
    public async Task CancelarEExcluirVenda_DevemFicarRegistradosNaAuditoria()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var produto = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta Log {sufixo}", 30m, 10m);

        var vendaCancelada = await infra.VenderAsync(produto.Id, 1m, 30m);
        var vendaExcluida = await infra.VenderAsync(produto.Id, 1m, 30m);

        var vendaService = infra.Servico<IVendaService>();
        await vendaService.CancelarAsync(vendaCancelada.Id);
        await vendaService.ExcluirFisicamenteAsync(vendaExcluida.Id);

        await using var ctx = await infra.ContextFactory.CreateDbContextAsync();
        var logsCancelada = await ctx.LogsAuditoria.AsNoTracking()
            .Where(l => l.Descricao.Contains(vendaCancelada.NumeroVenda) && l.Acao != "VENDA_FINALIZADA")
            .Select(l => l.Acao).ToListAsync();
        var logsExcluida = await ctx.LogsAuditoria.AsNoTracking()
            .Where(l => l.Descricao.Contains(vendaExcluida.NumeroVenda) && l.Acao != "VENDA_FINALIZADA")
            .Select(l => l.Acao).ToListAsync();

        Assert.True(logsCancelada.Count > 0 && logsExcluida.Count > 0,
            $"logs de auditoria — cancelamento: {logsCancelada.Count}, exclusão permanente: {logsExcluida.Count}.");
        Assert.Contains("VENDA_CANCELADA", logsCancelada);
        Assert.Contains("VENDA_EXCLUIDA_PERMANENTEMENTE", logsExcluida);
    }

    /// <summary>
    /// CORRIGIDO (M8). Antes a troca creditava o preço cheio: um item vendido com desconto
    /// devolvia mais do que o cliente pagou. Venda de 2 × R$ 100 com R$ 20 de desconto geral →
    /// devolver 1 credita R$ 90, e a diferença para um produto de R$ 100 é R$ 10 a receber.
    /// </summary>
    [Fact]
    public async Task TrocaDeItemVendidoComDesconto_CreditaOValorPago()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var produtoA = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta A {sufixo}", 100m, 10m);
        var produtoB = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta B {sufixo}", 100m, 10m);

        var venda = await infra.Servico<IVendaService>().CriarAsync(new CriarVendaDto
        {
            ConsumidorFinal = true,
            Usuario = "auditoria2",
            Desconto = 20m,
            Pagamentos = [new CriarVendaPagamentoDto { FormaPagamento = FormaPagamento.Pix, Valor = 180m }],
            Itens = [new CriarItemVendaDto { ProdutoId = produtoA.Id, Quantidade = 2m, PrecoUnitario = 100m }]
        });
        infra.RegistrarVenda(venda.Id);
        infra.RegistrarMarcadorAuditoria(venda.NumeroVenda);

        var troca = await infra.Servico<ITrocaService>().RegistrarAsync(new RegistrarTrocaDto
        {
            VendaOrigemId = venda.Id,
            ItemVendaOrigemId = venda.Itens[0].Id,
            QuantidadeDevolvida = 1m,
            RetornarAoEstoque = true,
            ProdutoNovoId = produtoB.Id,
            QuantidadeNova = 1m,
            PrecoUnitarioNovo = 100m,
            Usuario = "auditoria2"
        });

        Assert.Equal(90m, troca.ValorUnitarioDevolucao);
        Assert.Equal(90m, troca.ValorTotalDevolvido);
        Assert.Equal(10m, troca.DiferencaValor);

        await using var ctx = await infra.ContextFactory.CreateDbContextAsync();
        Assert.Equal(90m, await ctx.Trocas.AsNoTracking().Where(t => t.Id == troca.Id).Select(t => t.ValorUnitarioDevolucao).SingleAsync());
    }

    /// <summary>
    /// CORRIGIDO (M9). O produto e a movimentação "Estoque inicial" agora são gravados no mesmo
    /// SaveChanges — não existe mais produto com estoque sem a movimentação que o explica.
    /// </summary>
    [Fact]
    public async Task CadastrarProdutoComEstoque_GravaAMovimentacaoInicialJunto()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var produto = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta Inicial {sufixo}", 50m, 7.5m);

        await using var ctx = await infra.ContextFactory.CreateDbContextAsync();
        var movimentacao = await ctx.MovimentacoesEstoque.AsNoTracking().SingleAsync(m => m.ProdutoId == produto.Id);

        Assert.Equal(TipoMovimentacao.Entrada, movimentacao.Tipo);
        Assert.Equal("Estoque inicial", movimentacao.Motivo);
        Assert.Equal(0m, movimentacao.QuantidadeAnterior);
        Assert.Equal(7.5m, movimentacao.QuantidadeAtual);
        Assert.Equal(7.5m, await infra.EstoqueAsync(produto.Id));
    }

    /// <summary>
    /// M9 sob concorrência: cadastros com o mesmo código automático colidem no índice único e o
    /// repositório regenera o código e tenta de novo. Com a movimentação inicial pendurada no
    /// produto, a nova tentativa quebrava o EF (5 de 6 falhavam). Três simultâneos é o cenário
    /// realista de PDVs; com seis, 1 falha mesmo sem movimentação — limite anterior das 5 tentativas
    /// de gerar código, fora deste item.
    /// </summary>
    [Fact]
    public async Task CadastrosSimultaneosComCodigoAutomatico_RegeneramOCodigoSemPerderAMovimentacao()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var codigo = await infra.Servico<IProdutoService>().GerarProximoCodigoInternoAsync();

        async Task<(ProdutoDto? Produto, Exception? Erro)> CadastrarAsync(int n)
        {
            await using var scope = infra.Provider.CreateAsyncScope();
            try
            {
                var produto = await scope.ServiceProvider.GetRequiredService<IProdutoService>().CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
                    Nome = $"Aud2 Concorrente {n} {sufixo}",
                    CodigoInterno = codigo,
                    CodigoInternoDefinidoManualmente = false,
                    CategoriaId = cat,
                    MarcaId = marca,
                    PrecoVenda = 10m,
                    QuantidadeEstoque = 5m,
                    EstoqueMinimo = 0
                });
                return (produto, null);
            }
            catch (Exception ex)
            {
                return (null, ex);
            }
        }

        var resultados = await Task.WhenAll(Enumerable.Range(1, 3).Select(CadastrarAsync));
        foreach (var r in resultados.Where(r => r.Produto is not null))
            infra.RegistrarProduto(r.Produto!.Id);

        var erros = resultados.Where(r => r.Erro is not null).Select(r => r.Erro!.Message).ToList();
        Assert.True(erros.Count == 0, string.Join(" | ", erros));

        var ids = resultados.Select(r => r.Produto!.Id).ToList();
        await using var ctx = await infra.ContextFactory.CreateDbContextAsync();
        var movimentacoesPorProduto = await ctx.MovimentacoesEstoque.AsNoTracking()
            .Where(m => ids.Contains(m.ProdutoId))
            .GroupBy(m => m.ProdutoId)
            .Select(g => g.Count())
            .ToListAsync();

        Assert.Equal(3, movimentacoesPorProduto.Count);
        Assert.All(movimentacoesPorProduto, total => Assert.Equal(1, total));
        Assert.Equal(3, resultados.Select(r => r.Produto!.CodigoInterno).Distinct().Count());
    }

    /// <summary>A2: a troca também passa a deixar registro de quem fez e do que foi trocado.</summary>
    [Fact]
    public async Task RegistrarTroca_DeveFicarRegistradaNaAuditoria()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var produtoA = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta A {sufixo}", 80m, 10m);
        var produtoB = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta B {sufixo}", 80m, 10m);
        var venda = await infra.VenderAsync(produtoA.Id, 1m, 80m);

        await infra.Servico<ITrocaService>().RegistrarAsync(new RegistrarTrocaDto
        {
            VendaOrigemId = venda.Id,
            ItemVendaOrigemId = venda.Itens[0].Id,
            QuantidadeDevolvida = 1m,
            RetornarAoEstoque = true,
            ProdutoNovoId = produtoB.Id,
            QuantidadeNova = 1m,
            PrecoUnitarioNovo = 80m,
            Usuario = "auditoria2"
        });

        await using var ctx = await infra.ContextFactory.CreateDbContextAsync();
        var log = await ctx.LogsAuditoria.AsNoTracking()
            .SingleAsync(l => l.Acao == "TROCA_REGISTRADA" && l.Descricao.Contains(venda.NumeroVenda));

        Assert.Equal("auditoria2", log.NomeUsuario);
        Assert.Contains(produtoB.Nome, log.Descricao);
    }

    /// <summary>
    /// CORRIGIDO (A4), lado do servidor: 1,5 × R$ 9,99 agora vale R$ 14,99 no item, na venda e no
    /// pagamento exigido — o mesmo valor que o PDV cobra.
    /// </summary>
    [Fact]
    public async Task VendaFracionadaComPrecoQuebrado_FechaComPagamentoEmCentavos()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var sufixo = Sufixo();
        var (cat, marca) = await infra.CriarCatalogoAsync(sufixo);
        var produto = await infra.CriarProdutoAsync(cat, marca, $"Aud2 Tinta Litro {sufixo}", 9.99m, 10m);

        var venda = await infra.Servico<IVendaService>().CriarAsync(new CriarVendaDto
        {
            ConsumidorFinal = true,
            Usuario = "auditoria2",
            Pagamentos = [new CriarVendaPagamentoDto { FormaPagamento = FormaPagamento.Pix, Valor = 14.99m }],
            Itens = [new CriarItemVendaDto { ProdutoId = produto.Id, Quantidade = 1.5m, PrecoUnitario = 9.99m }]
        });
        infra.RegistrarVenda(venda.Id);
        infra.RegistrarMarcadorAuditoria(venda.NumeroVenda);

        Assert.Equal(14.99m, venda.Total);
        Assert.Equal(14.99m, venda.Itens[0].Subtotal);
        Assert.Equal(8.5m, await infra.EstoqueAsync(produto.Id));
    }

    /// <summary>
    /// Mesmo defeito corrigido hoje no Orçamento, ainda presente na Venda Externa:
    /// GerarNumeroVendaExternaAsync lê MAX+1 fora de transação e sem advisory lock.
    /// </summary>
    [Fact]
    public async Task RegistrarVendasExternasSimultaneas_NenhumaDeveFalhar()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        async Task<(VendaExternaDto? Venda, Exception? Erro)> TentarAsync(int n)
        {
            await using var scope = infra.Provider.CreateAsyncScope();
            try
            {
                var venda = await scope.ServiceProvider.GetRequiredService<IVendaExternaService>().RegistrarAsync(
                    new RegistrarVendaExternaDto
                    {
                        Observacoes = "auditoria2",
                        Usuario = "auditoria2",
                        Itens = [new RegistrarItemVendaExternaDto { NomeProduto = $"Item avulso {n}", Quantidade = 1m, PrecoBase = 10m, PrecoUnitario = 10m }]
                    });
                return (venda, null);
            }
            catch (Exception ex)
            {
                return (null, ex);
            }
        }

        var resultados = await Task.WhenAll(Enumerable.Range(1, 5).Select(TentarAsync));
        foreach (var r in resultados.Where(r => r.Venda is not null))
            infra.RegistrarVendaExterna(r.Venda!.Id);

        var falhas = resultados.Count(r => r.Erro is not null);
        Assert.True(falhas == 0,
            $"{falhas} de 5 vendas externas simultâneas falharam: " +
            string.Join(" | ", resultados.Where(r => r.Erro is not null).Select(r => r.Erro!.GetType().Name).Distinct()));
    }

    /// <summary>
    /// O controle de estado adicionado hoje em AlterarStatusAsync não cobre AtualizarAsync:
    /// um orçamento já APROVADO pelo cliente pode ter itens, preços e desconto reescritos, e o
    /// status continua "Aprovado" — o registro deixa de refletir o que o cliente aprovou.
    /// </summary>
    [Fact]
    public async Task EditarOrcamentoJaAprovado_DeveSerBloqueado()
    {
        await using var infra = await Auditoria2Infra.CriarAsync();
        if (infra is null) return;

        var servico = infra.Servico<IOrcamentoService>();
        var orcamento = await servico.RegistrarAsync(new RegistrarOrcamentoDto
        {
            NomeCliente = "Auditoria2 Aprovado",
            DataValidade = DateTime.Today.AddDays(5),
            Itens = [new ItemOrcamentoEntradaDto { NomeProduto = "Item", Quantidade = 1m, PrecoUnitario = 100m }]
        });
        infra.RegistrarOrcamento(orcamento.Id);
        infra.RegistrarMarcadorAuditoria(orcamento.NumeroOrcamento);

        await servico.AlterarStatusAsync(orcamento.Id, StatusOrcamento.Aprovado);

        var erro = await Record.ExceptionAsync(() => servico.AtualizarAsync(new AtualizarOrcamentoDto
        {
            Id = orcamento.Id,
            NomeCliente = "Auditoria2 Aprovado",
            DataValidade = DateTime.Today.AddDays(5),
            Desconto = 90m,
            Itens = [new ItemOrcamentoEntradaDto { NomeProduto = "Item", Quantidade = 1m, PrecoUnitario = 100m }]
        }));

        Assert.True(erro is DomainException,
            "orçamento APROVADO teve o desconto alterado de 0 para 90 e continuou marcado como aprovado.");
    }
}
