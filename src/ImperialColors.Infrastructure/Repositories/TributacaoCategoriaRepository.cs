using ImperialColors.Domain.Helpers;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ImperialColors.Infrastructure.Repositories;

public class TributacaoCategoriaRepository : ITributacaoCategoriaRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TributacaoCategoriaRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<TributacaoCategoria?> ObterPorCategoriaIdAsync(int categoriaId, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();
        return await context.Set<TributacaoCategoria>()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.CategoriaId == categoriaId, cancellationToken);
    }

    public async Task<TributacaoCategoria> SalvarAsync(TributacaoCategoria tributacao, CancellationToken cancellationToken = default)
    {
        await using var context = _contextFactory.CreateDbContext();

        var categoriaExiste = await context.Set<Categoria>()
            .AnyAsync(c => c.Id == tributacao.CategoriaId, cancellationToken);
        if (!categoriaExiste)
            throw new DomainException($"Categoria com Id {tributacao.CategoriaId} não encontrada.");

        var existente = await context.Set<TributacaoCategoria>()
            .FirstOrDefaultAsync(t => t.CategoriaId == tributacao.CategoriaId, cancellationToken);

        if (existente is null)
        {
            tributacao.AtualizadoEm = Relogio.Agora;
            await context.Set<TributacaoCategoria>().AddAsync(tributacao, cancellationToken);
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
