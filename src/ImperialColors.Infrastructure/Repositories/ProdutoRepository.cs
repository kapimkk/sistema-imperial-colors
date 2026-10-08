using ImperialColors.Application.Helpers;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Helpers;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;

namespace ImperialColors.Infrastructure.Repositories;

public class ProdutoRepository : RepositoryBase<Produto>, IProdutoRepository
{
    public ProdutoRepository(IDbContextFactory<AppDbContext> contextFactory) : base(contextFactory) { }

    public async Task<MovimentacaoEstoque> AjustarEstoqueTransacionalAsync(
        int produtoId,
        TipoMovimentacao tipo,
        decimal quantidade,
        string? motivo,
        string? usuario,
        CancellationToken cancellationToken = default)
    {
        return await ExecutarEmTransacaoAsync(async context =>
        {
            decimal qtdAnterior, qtdAtual;

            switch (tipo)
            {
                case TipoMovimentacao.Entrada:
                    (qtdAnterior, qtdAtual) = await EstoqueAtomicoHelper.ReporAsync(
                        context, produtoId, quantidade, cancellationToken);
                    break;

                case TipoMovimentacao.Saida:
                    var nomeProduto = await context.Set<Produto>()
                        .Where(p => p.Id == produtoId)
                        .Select(p => p.Nome)
                        .FirstOrDefaultAsync(cancellationToken)
                        ?? throw new DomainException($"Produto com Id {produtoId} não encontrado.");

                    (qtdAnterior, qtdAtual) = await EstoqueAtomicoHelper.BaixarAsync(
                        context, produtoId, quantidade, nomeProduto, cancellationToken);
                    break;

                case TipoMovimentacao.Ajuste:
                    // Ajuste manual de inventário fixa um valor absoluto — não há "estoque
                    // insuficiente" a guardar, mas ainda assim passa por ExecuteUpdateAsync
                    // para manter o mesmo padrão atômico e capturar o valor anterior real.
                    var existe = await context.Set<Produto>()
                        .Where(p => p.Id == produtoId)
                        .Select(p => (decimal?)p.QuantidadeEstoque)
                        .FirstOrDefaultAsync(cancellationToken);
                    qtdAnterior = existe ?? throw new DomainException($"Produto com Id {produtoId} não encontrado.");

                    // Variável local, não Relogio.Agora direto na expression tree — ver
                    // comentário em EstoqueAtomicoHelper.BaixarAsync.
                    var agoraAjuste = Relogio.Agora;
                    await context.Set<Produto>()
                        .Where(p => p.Id == produtoId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.QuantidadeEstoque, quantidade)
                            .SetProperty(p => p.AtualizadoEm, agoraAjuste), cancellationToken);
                    qtdAtual = quantidade;
                    break;

                default:
                    throw new DomainException("Tipo de movimentação inválido.");
            }

            var movimentacao = new MovimentacaoEstoque
            {
                ProdutoId = produtoId,
                Tipo = tipo,
                Quantidade = tipo == TipoMovimentacao.Ajuste ? Math.Abs(qtdAtual - qtdAnterior) : quantidade,
                QuantidadeAnterior = qtdAnterior,
                QuantidadeAtual = qtdAtual,
                Motivo = motivo ?? string.Empty,
                Usuario = usuario
            };

            context.Set<MovimentacaoEstoque>().Add(movimentacao);
            await context.SaveChangesAsync(cancellationToken);
            return movimentacao;
        }, cancellationToken);
    }

    public async Task<Produto> AtualizarComAjusteEstoqueTransacionalAsync(
        Produto produto,
        decimal quantidadeBaseline,
        decimal quantidadeDesejada,
        string? motivoAjuste,
        string? usuarioAjuste,
        CancellationToken cancellationToken = default,
        bool alterarImagem = false)
    {
        return await ExecutarEmTransacaoAsync(async context =>
        {
            // Atualiza os campos comerciais (nome, preço, categoria, etc.) mas exclui
            // QuantidadeEstoque do UPDATE — essa coluna é ajustada abaixo, de forma
            // atômica, pelo delta pretendido pelo usuário (não por um valor absoluto
            // obsoleto que estava em memória quando a tela de edição foi aberta).
            context.Set<Produto>().Update(produto);
            context.Entry(produto).Property(p => p.QuantidadeEstoque).IsModified = false;
            // Uma edição aberta antes de outra troca de imagem não pode restaurar referência antiga.
            if (!alterarImagem)
            {
                context.Entry(produto).Property(p => p.ImagemProdutoPath).IsModified = false;
                context.Entry(produto).Property(p => p.ImagemRemovida).IsModified = false;
            }
            await context.SaveChangesAsync(cancellationToken);

            // Delta = o que o usuário pretendia mudar (digitado - baseline que ele viu),
            // não a diferença contra o valor atual do banco. Editar sem tocar no campo de
            // quantidade dá delta = 0 e nunca mexe no estoque, mesmo que uma venda
            // concorrente já o tenha alterado.
            var delta = quantidadeDesejada - quantidadeBaseline;
            if (delta == 0)
            {
                var quantidadeAtualNoBanco = await context.Set<Produto>()
                    .Where(p => p.Id == produto.Id)
                    .Select(p => p.QuantidadeEstoque)
                    .FirstAsync(cancellationToken);
                produto.QuantidadeEstoque = quantidadeAtualNoBanco;
            }
            else
            {
                decimal qtdAnterior, qtdAtual;
                if (delta > 0)
                {
                    (qtdAnterior, qtdAtual) = await EstoqueAtomicoHelper.ReporAsync(
                        context, produto.Id, delta, cancellationToken);
                }
                else
                {
                    (qtdAnterior, qtdAtual) = await EstoqueAtomicoHelper.BaixarAsync(
                        context, produto.Id, -delta, produto.Nome, cancellationToken);
                }

                context.Set<MovimentacaoEstoque>().Add(new MovimentacaoEstoque
                {
                    ProdutoId = produto.Id,
                    Tipo = delta > 0 ? TipoMovimentacao.Entrada : TipoMovimentacao.Saida,
                    Quantidade = Math.Abs(delta),
                    QuantidadeAnterior = qtdAnterior,
                    QuantidadeAtual = qtdAtual,
                    Motivo = motivoAjuste ?? string.Empty,
                    Usuario = usuarioAjuste
                });
                await context.SaveChangesAsync(cancellationToken);
                produto.QuantidadeEstoque = qtdAtual;
            }

            if (!alterarImagem)
            {
                var imagemAtual = await context.Set<Produto>()
                    .Where(p => p.Id == produto.Id)
                    .Select(p => new {p.ImagemProdutoPath,p.ImagemRemovida})
                    .FirstAsync(cancellationToken);
                produto.ImagemProdutoPath = imagemAtual.ImagemProdutoPath;
                produto.ImagemRemovida = imagemAtual.ImagemRemovida;
            }
            return produto;
        }, cancellationToken);
    }

    public async Task<Produto?> ObterPorCodigoInternoAsync(string codigoInterno)
    {
        await using var context = ContextFactory.CreateDbContext();
        return await ConsultaLeituraComIncludes(context)
            .FirstOrDefaultAsync(p => p.CodigoInterno == codigoInterno);
    }

    public async Task<Produto?> ObterPorCodigoBarrasAsync(string codigoBarras)
    {
        await using var context = ContextFactory.CreateDbContext();
        return await ConsultaLeituraComIncludes(context)
            .FirstOrDefaultAsync(p => p.CodigoBarras == codigoBarras);
    }

    public async Task<IEnumerable<Produto>> BuscarPorNomeAsync(string nome)
    {
        await using var context = ContextFactory.CreateDbContext();
        return await ConsultaLeituraComIncludes(context)
            .Where(p => EF.Functions.ILike(p.Nome, $"%{nome}%"))
            .OrderBy(p => p.Nome)
            .ToListAsync();
    }

    public async Task<IEnumerable<Produto>> ObterComEstoqueBaixoAsync()
    {
        await using var context = ContextFactory.CreateDbContext();
        return await ConsultaLeituraComIncludes(context)
            .Where(p => p.QuantidadeEstoque <= p.EstoqueMinimo && p.QuantidadeEstoque > 0)
            .OrderBy(p => p.Nome)
            .ToListAsync();
    }

    public async Task<IEnumerable<Produto>> ObterSemEstoqueAsync()
    {
        await using var context = ContextFactory.CreateDbContext();
        return await ConsultaLeituraComIncludes(context)
            .Where(p => p.QuantidadeEstoque <= 0)
            .OrderBy(p => p.Nome)
            .ToListAsync();
    }

    public async Task<IEnumerable<Produto>> ObterProximosDaValidadeAsync(int diasLimite = 15)
    {
        var limite = DateTime.Today.AddDays(diasLimite);

        await using var context = ContextFactory.CreateDbContext();
        return await ConsultaLeituraComIncludes(context)
            .Where(p => p.QuantidadeEstoque > 0 && p.DataValidade != null && p.DataValidade <= limite)
            .OrderBy(p => p.DataValidade)
            .ToListAsync();
    }

    // As duas contagens abaixo consultam a tabela direto, sem ConsultaLeituraComIncludes: os
    // Include(Categoria/Marca/Fornecedor) existem para montar o DTO de listagem e não têm uso
    // nenhum num COUNT.
    public async Task<int> ContarComEstoqueCriticoAsync(decimal limiteUnidades = 5)
    {
        await using var context = ContextFactory.CreateDbContext();
        return await context.Set<Produto>()
            .AsNoTracking()
            .CountAsync(p => p.QuantidadeEstoque > 0 && p.QuantidadeEstoque < limiteUnidades);
    }

    public async Task<int> ContarSemEstoqueAsync(CancellationToken cancellationToken = default)
    {
        await using var context = ContextFactory.CreateDbContext();
        return await context.Set<Produto>()
            .AsNoTracking()
            .CountAsync(p => p.QuantidadeEstoque <= 0, cancellationToken);
    }

    public async Task<IEnumerable<Produto>> ObterComCategoriaEMarcaAsync()
    {
        await using var context = ContextFactory.CreateDbContext();
        return await ConsultaLeituraComIncludes(context)
            .OrderBy(p => p.Nome)
            .ToListAsync();
    }

    public async Task<(IReadOnlyList<Produto> Itens, int Total)> ObterPaginadoAsync(
        int pagina,
        int itensPorPagina,
        string? termoBusca = null,
        bool apenasPromocao = false,
        CancellationToken cancellationToken = default)
    {
        pagina = Math.Max(1, pagina);
        itensPorPagina = Math.Clamp(itensPorPagina, 1, 200);

        await using var context = ContextFactory.CreateDbContext();
        var query = ConsultaLeituraComIncludes(context);

        if (apenasPromocao)
        {
            query = query.Where(p =>
                p.PromocaoAtiva
                && p.PrecoPromocional.HasValue
                && p.PrecoPromocional.Value > 0
                && p.PrecoPromocional.Value < p.PrecoVenda);
        }

        if (!string.IsNullOrWhiteSpace(termoBusca))
        {
            var termo = termoBusca.Trim();
            query = query.Where(p =>
                EF.Functions.ILike(p.Nome, $"%{termo}%") ||
                p.CodigoInterno == termo ||
                (p.CodigoBarras != null && p.CodigoBarras == termo));
        }

        var total = await query.CountAsync(cancellationToken);
        var itens = await query
            .OrderBy(p => p.Nome)
            .Skip((pagina - 1) * itensPorPagina)
            .Take(itensPorPagina)
            .ToListAsync(cancellationToken);

        return (itens, total);
    }

    public async Task<bool> CodigoInternoExisteAsync(string codigoInterno, int? ignorarId = null)
    {
        await using var context = ContextFactory.CreateDbContext();
        return await context.Set<Produto>().IgnoreQueryFilters().AnyAsync(p =>
            p.CodigoInterno == codigoInterno &&
            (ignorarId == null || p.Id != ignorarId));
    }

    public async Task<bool> CodigoBarrasExisteAsync(
        string codigoBarras,
        int? ignorarId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(codigoBarras))
            return false;

        await using var context = ContextFactory.CreateDbContext();
        return await context.Set<Produto>()
            .IgnoreQueryFilters()
            .AnyAsync(p =>
                p.CodigoBarras != null &&
                p.CodigoBarras == codigoBarras &&
                (ignorarId == null || p.Id != ignorarId),
                cancellationToken);
    }

    public async Task<bool> PossuiHistoricoComercialAsync(
        int produtoId,
        CancellationToken cancellationToken = default)
    {
        await using var context = ContextFactory.CreateDbContext();

        if (await context.Set<ItemVenda>().IgnoreQueryFilters()
                .AnyAsync(i => i.ProdutoId == produtoId, cancellationToken))
            return true;

        if (await context.Set<ItemVendaExterna>().IgnoreQueryFilters()
                .AnyAsync(i => i.ProdutoId == produtoId, cancellationToken))
            return true;

        if (await context.Set<ItemListaCompra>().IgnoreQueryFilters()
                .AnyAsync(i => i.ProdutoId == produtoId, cancellationToken))
            return true;

        if (await context.Set<Troca>().IgnoreQueryFilters()
                .AnyAsync(t => t.ProdutoDevolvidoId == produtoId || t.ProdutoNovoId == produtoId, cancellationToken))
            return true;

        return false;
    }

    public async Task RemoverFisicamenteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = ContextFactory.CreateDbContext();

        var produto = await context.Set<Produto>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (produto is null)
            return;

        var movimentacoes = await context.Set<MovimentacaoEstoque>()
            .IgnoreQueryFilters()
            .Where(m => m.ProdutoId == id)
            .ToListAsync(cancellationToken);

        if (movimentacoes.Count > 0)
            context.Set<MovimentacaoEstoque>().RemoveRange(movimentacoes);

        context.Set<Produto>().Remove(produto);
        await SalvarAlteracoesAsync(context);
    }

    public async Task<bool> ExisteFisicamenteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var context = ContextFactory.CreateDbContext();
        return await context.Set<Produto>()
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<int> ObterMaiorSequenciaCodigoInternoAsync()
    {
        await using var context = ContextFactory.CreateDbContext();
        return await context.Database
            .SqlQuery<int>($"""
                SELECT COALESCE(MAX(CAST(SUBSTRING(codigo_interno FROM 2) AS INTEGER)), 0) AS "Value"
                FROM produtos
                WHERE codigo_interno ~ '^P[0-9]+$'
                """)
            .SingleAsync();
    }

    public async Task<int> ObterMaiorSequenciaPorSiglaAsync(string sigla, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sigla))
            return 0;

        var siglaUpper = sigla.ToUpperInvariant();
        await using var context = ContextFactory.CreateDbContext();
        var codigos = await context.Set<Produto>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.CodigoInterno.StartsWith(siglaUpper) && p.CodigoInterno.Length == siglaUpper.Length + 3)
            .Select(p => p.CodigoInterno)
            .ToListAsync(cancellationToken);

        var maior = 0;
        foreach (var codigo in codigos)
        {
            if (ProdutoCodigoIniciaisHelper.TryExtrairSequencia(codigo, siglaUpper, out var sequencia))
                maior = Math.Max(maior, sequencia);
        }

        return maior;
    }

    public async Task<Produto> InserirProdutoAsync(
        Produto produto,
        bool permitirRegenerarCodigoInterno,
        Func<Task<string>> obterProximoCodigoInternoAsync)
    {
        const int maxTentativas = 5;

        for (var tentativa = 0; tentativa < maxTentativas; tentativa++)
        {
            await using var context = ContextFactory.CreateDbContext();
            var dbSet = context.Set<Produto>();

            await GarantirCodigoInternoDisponivelAntesDeInserirAsync(
                produto,
                permitirRegenerarCodigoInterno,
                obterProximoCodigoInternoAsync);

            try
            {
                await dbSet.AddAsync(produto);
                await SalvarAlteracoesAsync(context);
                return produto;
            }
            catch (DomainException ex) when (
                permitirRegenerarCodigoInterno &&
                tentativa < maxTentativas - 1 &&
                DatabaseExceptionHelper.EhViolacaoUnicidadeCodigoInterno(ex))
            {
                // Clear em vez de desanexar só o produto: a movimentação de estoque inicial vai
                // pendurada nele, e desanexar o principal com o dependente rastreado quebra o EF.
                context.ChangeTracker.Clear();
                produto.CodigoInterno = await obterProximoCodigoInternoAsync();
            }
        }

        throw new DomainException("Não foi possível gerar um código interno único. Tente novamente.");
    }

    public override async Task<Produto?> ObterPorIdAsync(int id)
    {
        await using var context = ContextFactory.CreateDbContext();
        return await context.Set<Produto>()
            .Include(p => p.Categoria)
            .Include(p => p.Marca)
            .Include(p => p.Fornecedor)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    private async Task GarantirCodigoInternoDisponivelAntesDeInserirAsync(
        Produto produto,
        bool permitirRegenerarCodigoInterno,
        Func<Task<string>> obterProximoCodigoInternoAsync)
    {
        if (!await CodigoInternoExisteAsync(produto.CodigoInterno))
            return;

        if (!permitirRegenerarCodigoInterno)
            throw new DomainException("Este código interno já está em uso por outro produto.");

        produto.CodigoInterno = await obterProximoCodigoInternoAsync();

        if (await CodigoInternoExisteAsync(produto.CodigoInterno))
            throw new DomainException("Este código interno já está em uso por outro produto.");
    }

    private static IQueryable<Produto> ConsultaLeituraComIncludes(AppDbContext context)
        => context.Set<Produto>()
            .AsNoTracking()
            .Include(p => p.Categoria)
            .Include(p => p.Marca)
            .Include(p => p.Fornecedor);
}
