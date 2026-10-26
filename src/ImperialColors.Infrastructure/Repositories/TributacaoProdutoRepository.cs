using ImperialColors.Domain.Helpers;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ImperialColors.Infrastructure.Repositories;

public class TributacaoProdutoRepository : ITributacaoProdutoRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TributacaoProdutoRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<TributacaoProduto?> ObterPorProdutoIdAsync(int produtoId, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.Set<TributacaoProduto>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.ProdutoId == produtoId, cancellationToken);
    }

    public async Task<TributacaoProduto> SalvarAsync(TributacaoProduto tributacao, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var produtoExiste = await context.Set<Produto>()
            .AnyAsync(p => p.Id == tributacao.ProdutoId, cancellationToken);
        if (!produtoExiste)
            throw new DomainException($"Produto com Id {tributacao.ProdutoId} não encontrado.");

        var existente = await context.Set<TributacaoProduto>()
            .FirstOrDefaultAsync(t => t.ProdutoId == tributacao.ProdutoId, cancellationToken);

        if (existente is null)
        {
            tributacao.AtualizadoEm = Relogio.Agora;
            await context.Set<TributacaoProduto>().AddAsync(tributacao, cancellationToken);
        }
        else
        {
            existente.Ncm = tributacao.Ncm;
            existente.Cest = tributacao.Cest;
            existente.Origem = tributacao.Origem;
            existente.CstIcms = tributacao.CstIcms;
            existente.CsosnIcms = tributacao.CsosnIcms;
            existente.AliquotaIcms = tributacao.AliquotaIcms;
            existente.AliquotaIcmsSt = tributacao.AliquotaIcmsSt;
            existente.AliquotaIcmsStRetido = tributacao.AliquotaIcmsStRetido;
            existente.Mva = tributacao.Mva;
            existente.ReducaoBaseCalculo = tributacao.ReducaoBaseCalculo;
            existente.CstPis = tributacao.CstPis;
            existente.AliquotaPis = tributacao.AliquotaPis;
            existente.CstCofins = tributacao.CstCofins;
            existente.AliquotaCofins = tributacao.AliquotaCofins;
            existente.CstIpi = tributacao.CstIpi;
            existente.CodigoEnquadramentoIpi = tributacao.CodigoEnquadramentoIpi;
            existente.AliquotaIpi = tributacao.AliquotaIpi;
            existente.ValorIpiFixo = tributacao.ValorIpiFixo;
            existente.ExTipi = tributacao.ExTipi;
            existente.UnidadeTributavel = tributacao.UnidadeTributavel;
            existente.FatorConversao = tributacao.FatorConversao;
            existente.GtinTributavel = tributacao.GtinTributavel;
            existente.CfopDentroEstado = tributacao.CfopDentroEstado;
            existente.CfopForaEstado = tributacao.CfopForaEstado;
            existente.CstIbsCbs = tributacao.CstIbsCbs;
            existente.CClassTrib = tributacao.CClassTrib;
            existente.CstIS = tributacao.CstIS;
            existente.CClassTribIS = tributacao.CClassTribIS;
            existente.AliquotaIS = tributacao.AliquotaIS;
            existente.AliquotaIbsMunicipioDiferimento = tributacao.AliquotaIbsMunicipioDiferimento;
            existente.AliquotaIbsMunicipioReducao = tributacao.AliquotaIbsMunicipioReducao;
            existente.AtualizadoEm = Relogio.Agora;
            tributacao = existente;
        }

        await context.SaveChangesAsync(cancellationToken);
        return tributacao;
    }
}
