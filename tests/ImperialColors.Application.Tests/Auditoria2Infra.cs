using ImperialColors.Application.DTOs;
using ImperialColors.Application.Extensions;
using ImperialColors.Application.Interfaces;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Interfaces;
using ImperialColors.Infrastructure.Contingency;
using ImperialColors.Infrastructure.Data;
using ImperialColors.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Infra compartilhada da auditoria de 15/09 (segunda rodada).
///
/// Diferença importante para os outros testes de integração: o banco de contingência
/// (SQLite) é trocado por um arquivo temporário. <c>VendaService.CriarAsync</c> grava no
/// SQLite do PDV sempre que acha que o Postgres caiu — e um dos achados é justamente que ele
/// "acha" isso com erros que não são de conexão. Sem esta troca, o teste gravaria uma venda
/// pendente no pdv_contingency.db REAL da máquina, que o sistema tentaria sincronizar depois.
/// </summary>
internal sealed class Auditoria2Infra : IAsyncDisposable
{
    public ServiceProvider Provider { get; }
    public string CaminhoContingencia { get; }
    public IDbContextFactory<AppDbContext> ContextFactory => Provider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    private readonly List<int> _vendaIds = [];
    private readonly List<int> _vendaExternaIds = [];
    private readonly List<int> _trocaIds = [];
    private readonly List<int> _produtoIds = [];
    private readonly List<int> _categoriaIds = [];
    private readonly List<int> _marcaIds = [];
    private readonly List<int> _clienteIds = [];
    private readonly List<int> _notaIds = [];
    private readonly List<int> _orcamentoIds = [];
    private readonly List<string> _marcadoresAuditoria = [];

    private Auditoria2Infra(ServiceProvider provider, string caminhoContingencia)
    {
        Provider = provider;
        CaminhoContingencia = caminhoContingencia;
    }

    public static async Task<Auditoria2Infra?> CriarAsync()
    {
        if (!IntegrationTestGuard.TryObterConnectionString(out var cs))
            return null;

        var caminho = Path.Combine(Path.GetTempPath(), $"auditoria2_contingencia_{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Critical));
        services.AddInfrastructure(cs);
        services.AddApplication();

        foreach (var descritor in services
                     .Where(d => d.ServiceType.IsGenericType &&
                                 d.ServiceType.GenericTypeArguments.Contains(typeof(ContingencyDbContext)))
                     .ToList())
            services.Remove(descritor);

        services.AddDbContextFactory<ContingencyDbContext>(o => o.UseSqlite($"Data Source={caminho};Pooling=False"));

        var provider = services.BuildServiceProvider();

        await using (var ctx = await provider.GetRequiredService<IDbContextFactory<ContingencyDbContext>>().CreateDbContextAsync())
        {
            var conexao = ctx.Database.GetConnectionString() ?? string.Empty;
            if (!conexao.Contains(caminho, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Proteção: o banco de contingência do teste não é o arquivo temporário ({conexao}).");

            await ctx.Database.EnsureCreatedAsync();
        }

        return new Auditoria2Infra(provider, caminho);
    }

    public T Servico<T>() where T : notnull => Provider.GetRequiredService<T>();

    public void RegistrarVenda(int id) => _vendaIds.Add(id);
    public void RegistrarProduto(int id) => _produtoIds.Add(id);
    public void RegistrarVendaExterna(int id) => _vendaExternaIds.Add(id);
    public void RegistrarCliente(int id) => _clienteIds.Add(id);
    public void RegistrarNota(int id) => _notaIds.Add(id);
    public void RegistrarOrcamento(int id) => _orcamentoIds.Add(id);
    public void RegistrarMarcadorAuditoria(string marcador) => _marcadoresAuditoria.Add(marcador);

    public async Task<(int CategoriaId, int MarcaId)> CriarCatalogoAsync(string sufixo)
    {
        var categoria = await Servico<IRepository<Categoria>>().AdicionarAsync(new Categoria { Nome = $"CatAud2{sufixo}", Ativo = true });
        var marca = await Servico<IRepository<Marca>>().AdicionarAsync(new Marca { Nome = $"MarcaAud2{sufixo}", Ativo = true });
        _categoriaIds.Add(categoria.Id);
        _marcaIds.Add(marca.Id);
        return (categoria.Id, marca.Id);
    }

    public async Task<ProdutoDto> CriarProdutoAsync(int categoriaId, int marcaId, string nome, decimal preco, decimal estoque)
    {
        var produto = await Servico<IProdutoService>().CriarAsync(new CriarProdutoDto {
                    PesoGramas = 5500, AlturaCm = 25m, LarguraCm = 20m, ComprimentoCm = 30m,
            Nome = nome,
            CodigoInterno = $"AUD2-{Guid.NewGuid():N}"[..20],
            CodigoInternoDefinidoManualmente = true,
            CategoriaId = categoriaId,
            MarcaId = marcaId,
            PrecoVenda = preco,
            QuantidadeEstoque = estoque,
            EstoqueMinimo = 0
        });
        _produtoIds.Add(produto.Id);
        return produto;
    }

    public async Task<VendaDto> VenderAsync(int produtoId, decimal quantidade, decimal preco)
    {
        var total = quantidade * preco;
        var venda = await Servico<IVendaService>().CriarAsync(new CriarVendaDto
        {
            ConsumidorFinal = true,
            Usuario = "auditoria2",
            Pagamentos = [new CriarVendaPagamentoDto { FormaPagamento = FormaPagamento.Pix, Valor = total }],
            Itens = [new CriarItemVendaDto { ProdutoId = produtoId, Quantidade = quantidade, PrecoUnitario = preco }]
        });

        if (venda.Id > 0)
        {
            _vendaIds.Add(venda.Id);
            _marcadoresAuditoria.Add(venda.NumeroVenda);
        }

        return venda;
    }

    public async Task<decimal> EstoqueAsync(int produtoId)
    {
        await using var ctx = await ContextFactory.CreateDbContextAsync();
        return await ctx.Produtos.IgnoreQueryFilters().Where(p => p.Id == produtoId)
            .Select(p => p.QuantidadeEstoque).FirstAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var ctx = await ContextFactory.CreateDbContextAsync();

            await ctx.Trocas.IgnoreQueryFilters()
                .Where(t => (t.VendaOrigemId != null && _vendaIds.Contains(t.VendaOrigemId.Value)) ||
                            (t.VendaExternaOrigemId != null && _vendaExternaIds.Contains(t.VendaExternaOrigemId.Value)) ||
                            _produtoIds.Contains(t.ProdutoDevolvidoId) || _produtoIds.Contains(t.ProdutoNovoId))
                .ExecuteDeleteAsync();

            if (_notaIds.Count > 0)
                await ctx.NotasFiscais.IgnoreQueryFilters().Where(n => _notaIds.Contains(n.Id)).ExecuteDeleteAsync();

            if (_orcamentoIds.Count > 0)
            {
                await ctx.ItensOrcamento.IgnoreQueryFilters().Where(i => _orcamentoIds.Contains(i.OrcamentoId)).ExecuteDeleteAsync();
                await ctx.Orcamentos.IgnoreQueryFilters().Where(o => _orcamentoIds.Contains(o.Id)).ExecuteDeleteAsync();
            }

            if (_vendaIds.Count > 0)
            {
                await ctx.MovimentacoesEstoque.IgnoreQueryFilters()
                    .Where(m => m.VendaId != null && _vendaIds.Contains(m.VendaId.Value)).ExecuteDeleteAsync();
                await ctx.Vendas.IgnoreQueryFilters().Where(v => _vendaIds.Contains(v.Id)).ExecuteDeleteAsync();
            }

            if (_vendaExternaIds.Count > 0)
            {
                await ctx.ItensVendaExterna.IgnoreQueryFilters()
                    .Where(i => _vendaExternaIds.Contains(i.VendaExternaId)).ExecuteDeleteAsync();
                await ctx.VendasExternas.IgnoreQueryFilters().Where(v => _vendaExternaIds.Contains(v.Id)).ExecuteDeleteAsync();
            }

            if (_produtoIds.Count > 0)
            {
                await ctx.ItensVenda.Where(i => _produtoIds.Contains(i.ProdutoId)).ExecuteDeleteAsync();
                await ctx.MovimentacoesEstoque.IgnoreQueryFilters()
                    .Where(m => _produtoIds.Contains(m.ProdutoId)).ExecuteDeleteAsync();
                await ctx.Produtos.IgnoreQueryFilters().Where(p => _produtoIds.Contains(p.Id)).ExecuteDeleteAsync();
            }

            if (_clienteIds.Count > 0)
                await ctx.Clientes.IgnoreQueryFilters().Where(c => _clienteIds.Contains(c.Id)).ExecuteDeleteAsync();
            if (_categoriaIds.Count > 0)
                await ctx.Categorias.IgnoreQueryFilters().Where(c => _categoriaIds.Contains(c.Id)).ExecuteDeleteAsync();
            if (_marcaIds.Count > 0)
                await ctx.Marcas.IgnoreQueryFilters().Where(m => _marcaIds.Contains(m.Id)).ExecuteDeleteAsync();

            foreach (var marcador in _marcadoresAuditoria.Where(m => !string.IsNullOrWhiteSpace(m)))
                await ctx.LogsAuditoria.Where(l => l.Descricao.Contains(marcador)).ExecuteDeleteAsync();
        }
        finally
        {
            await Provider.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(CaminhoContingencia); } catch (IOException) { }
        }
    }
}
