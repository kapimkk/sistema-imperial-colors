using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Interfaces;
using ImperialColors.Application.Security;
using ImperialColors.Application.Validation;
using ImperialColors.Domain.Constants;
using ImperialColors.Domain.Entities;
using ImperialColors.Domain.Enums;
using ImperialColors.Domain.Exceptions;
using ImperialColors.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImperialColors.Application.Services;

public class ProdutoService : IProdutoService
{
    public const string MensagemExclusaoBloqueadaPorHistorico =
        "Este produto não pode ser excluído permanentemente porque já possui movimentações ou vendas registradas no sistema.";

    public const string MensagemCodigoBarrasDuplicado =
        "Este código de barras já está cadastrado para outro produto.";

    private readonly IProdutoRepository _produtoRepository;
    private readonly IRepository<Categoria> _categoriaRepository;
    private readonly IRepository<Marca> _marcaRepository;
    private readonly ITributacaoProdutoRepository _tributacaoRepository;
    private readonly IConfiguracaoFiscalService _configuracaoFiscal;
    private readonly IAuditoriaService _auditoria;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly ILogger<ProdutoService> _logger;
    private readonly IImagemProdutoStorage? _imagens;

    public ProdutoService(
        IProdutoRepository produtoRepository,
        IRepository<Categoria> categoriaRepository,
        IRepository<Marca> marcaRepository,
        ITributacaoProdutoRepository tributacaoRepository,
        IConfiguracaoFiscalService configuracaoFiscal,
        IAuditoriaService auditoria,
        IUsuarioAtual usuarioAtual,
        ILogger<ProdutoService> logger,
        IImagemProdutoStorage? imagens = null)
    {
        _produtoRepository = produtoRepository;
        _categoriaRepository = categoriaRepository;
        _marcaRepository = marcaRepository;
        _tributacaoRepository = tributacaoRepository;
        _configuracaoFiscal = configuracaoFiscal;
        _auditoria = auditoria;
        _usuarioAtual = usuarioAtual;
        _logger = logger;
        _imagens = imagens;
    }

    public async Task<IEnumerable<ProdutoDto>> ObterTodosAsync()
    {
        var produtos = await _produtoRepository.ObterComCategoriaEMarcaAsync();
        return produtos.Select(MapParaDto);
    }

    public async Task<PaginacaoResultadoDto<ProdutoDto>> ObterPaginadoAsync(
        int pagina,
        int itensPorPagina,
        string? termoBusca = null,
        bool apenasPromocao = false,
        CancellationToken cancellationToken = default)
    {
        var (itens, total) = await _produtoRepository.ObterPaginadoAsync(
            pagina, itensPorPagina, termoBusca, apenasPromocao, cancellationToken);

        return new PaginacaoResultadoDto<ProdutoDto>
        {
            Itens = itens.Select(MapParaDto).ToList(),
            PaginaAtual = pagina,
            ItensPorPagina = itensPorPagina,
            TotalItens = total
        };
    }

    public async Task<ProdutoDto?> ObterPorIdAsync(int id)
    {
        var produto = await _produtoRepository.ObterPorIdAsync(id);
        return produto is null ? null : MapParaDto(produto);
    }

    public async Task<ProdutoDto?> ObterPorCodigoBarrasAsync(string codigoBarras)
    {
        var produto = await _produtoRepository.ObterPorCodigoBarrasAsync(codigoBarras);
        return produto is null ? null : MapParaDto(produto);
    }

    public async Task<ProdutoDto?> ObterPorCodigoInternoAsync(string codigoInterno)
    {
        var produto = await _produtoRepository.ObterPorCodigoInternoAsync(codigoInterno);
        return produto is null ? null : MapParaDto(produto);
    }

    public async Task<IEnumerable<ProdutoDto>> BuscarAsync(string termo)
    {
        if (string.IsNullOrWhiteSpace(termo))
            return await ObterTodosAsync();

        var porNome = await _produtoRepository.BuscarPorNomeAsync(termo);
        var porCodigo = await _produtoRepository.ObterPorCodigoInternoAsync(termo);
        var porBarras = await _produtoRepository.ObterPorCodigoBarrasAsync(termo);

        var resultado = porNome.ToList();
        if (porCodigo is not null && !resultado.Any(p => p.Id == porCodigo.Id))
            resultado.Add(porCodigo);
        if (porBarras is not null && !resultado.Any(p => p.Id == porBarras.Id))
            resultado.Add(porBarras);

        return resultado.Select(MapParaDto);
    }

    public async Task<ProdutoDto> CriarAsync(CriarProdutoDto dto)
    {
        ProdutoValidator.ValidarNovo(dto);
        await ValidarReferenciasCatalogoAsync(dto.CategoriaId!.Value, dto.MarcaId!.Value);
        await ValidarCodigoBarrasUnicoAsync(dto.CodigoBarras);

        var codigoInterno = InputSanitizer.SanitizarTexto(dto.CodigoInterno, 50);
        var codigoManual = dto.CodigoInternoDefinidoManualmente;

        if (await _produtoRepository.CodigoInternoExisteAsync(codigoInterno))
        {
            if (codigoManual)
                throw new DomainException("Este código interno já está em uso por outro produto.");

            codigoInterno = await GerarProximoCodigoInternoDisponivelAsync();
        }

        var unidade = UnidadesMedida.Normalizar(dto.Unidade);
        var produto = new Produto
        {
            CodigoInterno = codigoInterno,
            CodigoBarras = InputSanitizer.SanitizarTexto(dto.CodigoBarras, 50),
            Nome = InputSanitizer.SanitizarTexto(dto.Nome, 200),
            CategoriaId = dto.CategoriaId,
            MarcaId = dto.MarcaId,
            QuantidadeEstoque = dto.QuantidadeEstoque,
            EstoqueMinimo = dto.EstoqueMinimo,
            Unidade = unidade,
            TamanhoEmbalagem = InputSanitizer.SanitizarTexto(dto.TamanhoEmbalagem, 30),
            PesoGramas = dto.PesoGramas,
            AlturaCm = dto.AlturaCm,
            LarguraCm = dto.LarguraCm,
            ComprimentoCm = dto.ComprimentoCm,
            Custo = dto.Custo,
            PrecoVenda = dto.PrecoVenda,
            PromocaoAtiva = dto.PromocaoAtiva,
            PrecoPromocional = dto.PromocaoAtiva ? dto.PrecoPromocional : null,
            DataValidade = dto.DataValidade?.Date,
            FornecedorId = dto.FornecedorId,
            Observacoes = InputSanitizer.SanitizarTexto(dto.Observacoes, 10_000)
        };

        // Pela navegação, a movimentação entra no mesmo SaveChanges do produto: ou os dois gravam, ou nenhum.
        if (dto.QuantidadeEstoque > 0)
        {
            produto.Movimentacoes.Add(new MovimentacaoEstoque
            {
                Tipo = TipoMovimentacao.Entrada,
                Quantidade = dto.QuantidadeEstoque,
                QuantidadeAnterior = 0,
                QuantidadeAtual = dto.QuantidadeEstoque,
                Motivo = "Estoque inicial"
            });
        }

        using var bloqueioImagem = (dto.AlterarImagem || dto.RemoverImagem) && _imagens is not null
            ? await _imagens.AdquirirBloqueioAsync() : null;
        produto.ImagemProdutoPath = await PrepararImagemAsync(dto, null);
        produto.ImagemRemovida = dto.RemoverImagem;
        Produto criado;
        try
        {
            criado = await _produtoRepository.InserirProdutoAsync(
                produto,
                permitirRegenerarCodigoInterno: !codigoManual,
                obterProximoCodigoInternoAsync: () => RegenerarCodigoInternoAsync(produto.Nome, produto.CodigoInterno));
        }
        catch
        {
            if (dto.AlterarImagem) RemoverImagemSemInterromper(produto.ImagemProdutoPath);
            throw;
        }

        _logger.LogInformation("Produto criado: {Nome} ({CodigoInterno})", dto.Nome, criado.CodigoInterno);
        return MapParaDto(criado);
    }

    public async Task<ProdutoDto> AtualizarAsync(int id, AtualizarProdutoDto dto)
    {
        ProdutoValidator.Validar(dto);
        await ValidarReferenciasCatalogoAsync(dto.CategoriaId!.Value, dto.MarcaId!.Value);
        await ValidarCodigoBarrasUnicoAsync(dto.CodigoBarras, id);

        using var bloqueioImagem = (dto.AlterarImagem || dto.RemoverImagem) && _imagens is not null
            ? await _imagens.AdquirirBloqueioAsync() : null;
        var produto = await _produtoRepository.ObterPorIdAsync(id)
            ?? throw new DomainException($"Produto com Id {id} não encontrado.");

        // Prioriza o baseline que a tela de edição capturou ao carregar (protege contra o
        // formulário ficar aberto enquanto uma venda concorrente altera o estoque real);
        // sem esse valor, cai para o estoque atual do banco (ainda seguro para o caso de
        // duas gravações concorrentes na mesma janela desta chamada).
        var quantidadeBaseline = dto.QuantidadeEstoqueOriginal ?? produto.QuantidadeEstoque;

        if (await _produtoRepository.CodigoInternoExisteAsync(dto.CodigoInterno, id))
            throw new DomainException("Este código interno já está em uso por outro produto.");

        var precosAntes = (produto.PrecoVenda, produto.Custo, produto.PromocaoAtiva, produto.PrecoPromocional);

        produto.CodigoInterno = InputSanitizer.SanitizarTexto(dto.CodigoInterno, 50);
        produto.CodigoBarras = InputSanitizer.SanitizarTexto(dto.CodigoBarras, 50);
        produto.Nome = InputSanitizer.SanitizarTexto(dto.Nome, 200);
        produto.CategoriaId = dto.CategoriaId;
        produto.MarcaId = dto.MarcaId;
        produto.EstoqueMinimo = dto.EstoqueMinimo;
        var unidadeAtualizada = UnidadesMedida.Normalizar(dto.Unidade);
        produto.Unidade = unidadeAtualizada;
        produto.TamanhoEmbalagem = InputSanitizer.SanitizarTexto(dto.TamanhoEmbalagem, 30);
        produto.PesoGramas = dto.PesoGramas;
        produto.AlturaCm = dto.AlturaCm;
        produto.LarguraCm = dto.LarguraCm;
        produto.ComprimentoCm = dto.ComprimentoCm;
        produto.Custo = dto.Custo;
        produto.PrecoVenda = dto.PrecoVenda;
        produto.PromocaoAtiva = dto.PromocaoAtiva;
        produto.PrecoPromocional = dto.PromocaoAtiva ? dto.PrecoPromocional : null;
        produto.DataValidade = dto.DataValidade?.Date;
        produto.FornecedorId = dto.FornecedorId;
        produto.Observacoes = InputSanitizer.SanitizarTexto(dto.Observacoes, 10_000);

        // Campos comerciais + ajuste de estoque (se a quantidade foi alterada na tela)
        // são gravados em uma única transação, com o delta de estoque calculado contra
        // o valor real e atual do banco — não contra o valor que estava em memória quando
        // o formulário foi aberto. Isso evita que salvar a edição de um produto apague
        // silenciosamente uma baixa feita por uma venda concorrente no PDV.
        var imagemAnterior = produto.ImagemProdutoPath;
        var novaImagem = await PrepararImagemAsync(dto, imagemAnterior);
        produto.ImagemProdutoPath = novaImagem;
        if (dto.RemoverImagem) produto.ImagemRemovida = true;
        else if (dto.AlterarImagem) produto.ImagemRemovida = false;
        Produto atualizado;
        try
        {
            atualizado = await _produtoRepository.AtualizarComAjusteEstoqueTransacionalAsync(
                produto, quantidadeBaseline, dto.QuantidadeEstoque, "Ajuste manual via edição de produto", "Administrador",
                alterarImagem: dto.AlterarImagem || dto.RemoverImagem);
        }
        catch
        {
            if (dto.AlterarImagem) RemoverImagemSemInterromper(novaImagem);
            throw;
        }
        if ((dto.AlterarImagem || dto.RemoverImagem) && imagemAnterior != novaImagem)
            RemoverImagemSemInterromper(imagemAnterior);

        _logger.LogInformation("Produto atualizado: {Nome} ({Id})", dto.Nome, id);

        var precosDepois = (produto.PrecoVenda, produto.Custo, produto.PromocaoAtiva, produto.PrecoPromocional);
        if (precosDepois != precosAntes)
        {
            await RegistrarAuditoriaEstoqueAsync(
                "PRODUTO_PRECO_ALTERADO",
                $"Preços de '{produto.Nome}' alterados — venda {precosAntes.PrecoVenda:C} → {precosDepois.PrecoVenda:C}, " +
                $"custo {precosAntes.Custo:C} → {precosDepois.Custo:C}, " +
                $"promoção {DescreverPromocao(precosAntes.PromocaoAtiva, precosAntes.PrecoPromocional)} → " +
                $"{DescreverPromocao(precosDepois.PromocaoAtiva, precosDepois.PrecoPromocional)}",
                NivelLogAuditoria.Info,
                new
                {
                    ProdutoId = id,
                    Antes = new { precosAntes.PrecoVenda, precosAntes.Custo, precosAntes.PromocaoAtiva, precosAntes.PrecoPromocional },
                    Depois = new { precosDepois.PrecoVenda, precosDepois.Custo, precosDepois.PromocaoAtiva, precosDepois.PrecoPromocional }
                });
        }

        if (dto.QuantidadeEstoque != quantidadeBaseline)
        {
            await RegistrarAuditoriaEstoqueAsync(
                "ESTOQUE_AJUSTE_EDICAO",
                $"Estoque de '{produto.Nome}' ajustado pela edição do produto: {quantidadeBaseline:0.###} → {dto.QuantidadeEstoque:0.###}",
                NivelLogAuditoria.Warning,
                new { ProdutoId = id, QuantidadeNaTela = quantidadeBaseline, QuantidadeInformada = dto.QuantidadeEstoque, EstoqueFinal = atualizado.QuantidadeEstoque });
        }

        return MapParaDto(atualizado);
    }

    private Task<string?> PrepararImagemAsync(CriarProdutoDto dto, string? anterior)
    {
        if (dto.RemoverImagem) return Task.FromResult<string?>(null);
        if (!dto.AlterarImagem) return Task.FromResult(anterior);
        if (_imagens is null || string.IsNullOrWhiteSpace(dto.ImagemArquivoSelecionado))
            throw new DomainException("Selecione uma imagem válida antes de salvar.");
        return ImportarImagemAsync(dto.ImagemArquivoSelecionado);
    }

    private async Task<string?> ImportarImagemAsync(string origem)
        => await _imagens!.ImportarAsync(origem);

    private void RemoverImagemSemInterromper(string? referencia)
    {
        if (_imagens is null || string.IsNullOrWhiteSpace(referencia)) return;
        try { _imagens.RemoverSeExistir(referencia); }
        catch { _logger.LogWarning("Não foi possível limpar um arquivo de imagem do catálogo. Verifique o backup local."); }
    }

    private static string DescreverPromocao(bool ativa, decimal? preco)
        => ativa ? $"ativa ({preco:C})" : "inativa";

    private Task RegistrarAuditoriaEstoqueAsync(string acao, string descricao, NivelLogAuditoria nivel, object payload, string? usuario = null)
        => _auditoria.RegistrarAsync(new RegistrarLogAuditoriaDto
        {
            NomeUsuario = string.IsNullOrWhiteSpace(usuario) ? _usuarioAtual.Nome : usuario,
            Modulo = "Estoque",
            Acao = acao,
            Descricao = descricao,
            Nivel = nivel,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload)
        });

    public async Task RemoverAsync(int id)
    {
        var produto = await _produtoRepository.ObterPorIdAsync(id)
            ?? throw new DomainException($"Produto com Id {id} não encontrado.");

        if (await _produtoRepository.PossuiHistoricoComercialAsync(id))
            throw new DomainException(MensagemExclusaoBloqueadaPorHistorico);

        await _produtoRepository.RemoverFisicamenteAsync(id);

        if (await _produtoRepository.ExisteFisicamenteAsync(id))
            throw new DomainException("Não foi possível excluir o produto. Tente novamente.");

        _logger.LogInformation("Produto excluído (hard delete): Id={Id}", id);

        await RegistrarAuditoriaEstoqueAsync(
            "PRODUTO_EXCLUIDO",
            $"Produto '{produto.Nome}' ({produto.CodigoInterno}) excluído permanentemente — estoque {produto.QuantidadeEstoque:0.###}",
            NivelLogAuditoria.Warning,
            new { produto.Id, produto.CodigoInterno, produto.CodigoBarras, produto.Nome, produto.QuantidadeEstoque, produto.PrecoVenda, produto.Custo });
    }

    public async Task<bool> CodigoBarrasExisteAsync(
        string codigoBarras,
        int? ignorarProdutoId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizado = InputSanitizer.SanitizarTexto(codigoBarras, 50);
        if (string.IsNullOrWhiteSpace(normalizado))
            return false;

        return await _produtoRepository.CodigoBarrasExisteAsync(normalizado, ignorarProdutoId, cancellationToken);
    }

    public async Task<IEnumerable<ProdutoDto>> ObterComEstoqueBaixoAsync()
    {
        var produtos = await _produtoRepository.ObterComEstoqueBaixoAsync();
        return produtos.Select(MapParaDto);
    }

    public async Task<IEnumerable<ProdutoDto>> ObterSemEstoqueAsync()
    {
        var produtos = await _produtoRepository.ObterSemEstoqueAsync();
        return produtos.Select(MapParaDto);
    }

    public async Task<IEnumerable<ProdutoDto>> ObterProximosDaValidadeAsync(int diasLimite = 15)
    {
        var produtos = await _produtoRepository.ObterProximosDaValidadeAsync(diasLimite);
        return produtos.Select(MapParaDto);
    }

    public async Task RegistrarMovimentacaoAsync(MovimentacaoEstoqueDto dto)
    {
        if (dto.Quantidade < 0)
            throw new DomainException("Quantidade inválida.");

        // Baixa/reposição/ajuste + registro da movimentação em uma única transação, com
        // UPDATE atômico guardado (nunca deixa o estoque negativo por concorrência com
        // uma venda simultânea no PDV — ver EstoqueAtomicoHelper).
        var movimentacao = await _produtoRepository.AjustarEstoqueTransacionalAsync(
            dto.ProdutoId, dto.Tipo, dto.Quantidade, dto.Motivo, dto.Usuario);

        var nome = (await _produtoRepository.ObterPorIdAsync(dto.ProdutoId))?.Nome ?? $"Id {dto.ProdutoId}";
        await RegistrarAuditoriaEstoqueAsync(
            "ESTOQUE_MOVIMENTACAO_MANUAL",
            $"{dto.Tipo} manual em '{nome}': {movimentacao.QuantidadeAnterior:0.###} → {movimentacao.QuantidadeAtual:0.###}" +
            (string.IsNullOrWhiteSpace(dto.Motivo) ? string.Empty : $" — motivo: {dto.Motivo}"),
            NivelLogAuditoria.Warning,
            new { dto.ProdutoId, Tipo = dto.Tipo.ToString(), dto.Quantidade, movimentacao.QuantidadeAnterior, movimentacao.QuantidadeAtual, dto.Motivo },
            dto.Usuario);
    }

    public async Task<string> GerarProximoCodigoInternoAsync()
        => await GerarProximoCodigoInternoDisponivelAsync();

    public async Task<string> GerarCodigoInternoPorNomeAsync(string nome, CancellationToken cancellationToken = default)
    {
        var sigla = ProdutoCodigoIniciaisHelper.ExtrairSigla(nome);
        if (string.IsNullOrWhiteSpace(sigla))
            return await GerarProximoCodigoInternoDisponivelAsync();

        var maiorSequencia = await _produtoRepository.ObterMaiorSequenciaPorSiglaAsync(sigla, cancellationToken);
        return ProdutoCodigoIniciaisHelper.FormatarCodigo(sigla, maiorSequencia + 1);
    }

    private async Task<string> RegenerarCodigoInternoAsync(string nome, string? codigoAtual)
    {
        if (!string.IsNullOrWhiteSpace(codigoAtual)
            && ProdutoCodigoIniciaisHelper.EhCodigoPorIniciais(codigoAtual))
        {
            var sigla = codigoAtual[..^3].ToUpperInvariant();
            var maiorSequencia = await _produtoRepository.ObterMaiorSequenciaPorSiglaAsync(sigla);
            return ProdutoCodigoIniciaisHelper.FormatarCodigo(sigla, maiorSequencia + 1);
        }

        return await GerarCodigoInternoPorNomeAsync(nome);
    }

    private async Task<string> GerarProximoCodigoInternoDisponivelAsync()
    {
        var maiorSequencia = await _produtoRepository.ObterMaiorSequenciaCodigoInternoAsync();
        return ProdutoCodigoInternoHelper.FormatarSequencia(maiorSequencia + 1);
    }

    private async Task ValidarCodigoBarrasUnicoAsync(string? codigoBarras, int? ignorarProdutoId = null)
    {
        if (await CodigoBarrasExisteAsync(codigoBarras ?? string.Empty, ignorarProdutoId))
            throw new DomainException(MensagemCodigoBarrasDuplicado);
    }

    private async Task ValidarReferenciasCatalogoAsync(int categoriaId, int marcaId)
    {
        if (categoriaId <= 0 || marcaId <= 0)
            throw new DomainException("Selecione uma Categoria e uma Marca válidas.");

        if (!await _categoriaRepository.ExisteAsync(categoriaId))
            throw new DomainException(
                $"A categoria selecionada (Id={categoriaId}) não existe no banco de dados. " +
                "Selecione ou cadastre uma categoria válida.");

        if (!await _marcaRepository.ExisteAsync(marcaId))
            throw new DomainException(
                $"A marca selecionada (Id={marcaId}) não existe no banco de dados. " +
                "Selecione ou cadastre uma marca válida.");
    }

    public async Task<TributacaoProdutoDto> ObterTributacaoAsync(int produtoId, CancellationToken cancellationToken = default)
    {
        var tributacao = await _tributacaoRepository.ObterPorProdutoIdAsync(produtoId, cancellationToken);
        return tributacao is null
            ? new TributacaoProdutoDto { ProdutoId = produtoId }
            : MapParaDto(tributacao);
    }

    public async Task<TributacaoProdutoDto> SalvarTributacaoAsync(
        int produtoId, TributacaoProdutoDto dto, CancellationToken cancellationToken = default)
    {
        if (!await _produtoRepository.ExisteAsync(produtoId))
            throw new DomainException($"Produto com Id {produtoId} não encontrado.");

        var regime = await _configuracaoFiscal.ObterRegimeAsync(cancellationToken);
        dto.ProdutoId = produtoId;
        TributacaoProdutoValidator.Validar(dto, regime);

        var entidade = new TributacaoProduto
        {
            ProdutoId = produtoId,
            Ncm = NormalizarDigitos(dto.Ncm),
            Cest = NormalizarDigitos(dto.Cest),
            Origem = dto.Origem,
            CstIcms = NormalizarDigitos(dto.CstIcms),
            CsosnIcms = NormalizarDigitos(dto.CsosnIcms),
            AliquotaIcms = dto.AliquotaIcms,
            AliquotaIcmsSt = dto.AliquotaIcmsSt,
            Mva = dto.Mva,
            ReducaoBaseCalculo = dto.ReducaoBaseCalculo,
            AliquotaIcmsStRetido = dto.AliquotaIcmsStRetido,
            CstPis = NormalizarDigitos(dto.CstPis),
            AliquotaPis = dto.AliquotaPis,
            CstCofins = NormalizarDigitos(dto.CstCofins),
            AliquotaCofins = dto.AliquotaCofins,
            CstIpi = NormalizarDigitos(dto.CstIpi),
            CodigoEnquadramentoIpi = NormalizarDigitos(dto.CodigoEnquadramentoIpi),
            AliquotaIpi = dto.AliquotaIpi,
            ValorIpiFixo = dto.ValorIpiFixo,
            ExTipi = NormalizarDigitos(dto.ExTipi),
            UnidadeTributavel = string.IsNullOrWhiteSpace(dto.UnidadeTributavel)
                ? null
                : dto.UnidadeTributavel.Trim().ToUpperInvariant(),
            FatorConversao = dto.FatorConversao,
            GtinTributavel = string.IsNullOrWhiteSpace(dto.GtinTributavel) ? null : dto.GtinTributavel.Trim(),
            CfopDentroEstado = NormalizarDigitos(dto.CfopDentroEstado),
            CfopForaEstado = NormalizarDigitos(dto.CfopForaEstado),
            CstIbsCbs = NormalizarDigitos(dto.CstIbsCbs),
            CClassTrib = NormalizarDigitos(dto.CClassTrib),
            CstIS = NormalizarDigitos(dto.CstIS),
            CClassTribIS = NormalizarDigitos(dto.CClassTribIS),
            AliquotaIS = dto.AliquotaIS,
            AliquotaIbsMunicipioDiferimento = dto.AliquotaIbsMunicipioDiferimento,
            AliquotaIbsMunicipioReducao = dto.AliquotaIbsMunicipioReducao
        };

        var salvo = await _tributacaoRepository.SalvarAsync(entidade, cancellationToken);
        _logger.LogInformation("Tributação salva para o produto Id={ProdutoId}", produtoId);
        return MapParaDto(salvo);
    }

    private static string? NormalizarDigitos(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static TributacaoProdutoDto MapParaDto(TributacaoProduto t) => new()
    {
        ProdutoId = t.ProdutoId,
        Ncm = t.Ncm,
        Cest = t.Cest,
        Origem = t.Origem,
        CstIcms = t.CstIcms,
        CsosnIcms = t.CsosnIcms,
        AliquotaIcms = t.AliquotaIcms,
        AliquotaIcmsSt = t.AliquotaIcmsSt,
        Mva = t.Mva,
        ReducaoBaseCalculo = t.ReducaoBaseCalculo,
        AliquotaIcmsStRetido = t.AliquotaIcmsStRetido,
        CstPis = t.CstPis,
        AliquotaPis = t.AliquotaPis,
        CstCofins = t.CstCofins,
        AliquotaCofins = t.AliquotaCofins,
        CstIpi = t.CstIpi,
        CodigoEnquadramentoIpi = t.CodigoEnquadramentoIpi,
        AliquotaIpi = t.AliquotaIpi,
        ValorIpiFixo = t.ValorIpiFixo,
        ExTipi = t.ExTipi,
        UnidadeTributavel = t.UnidadeTributavel,
        FatorConversao = t.FatorConversao,
        GtinTributavel = t.GtinTributavel,
        CfopDentroEstado = t.CfopDentroEstado,
        CfopForaEstado = t.CfopForaEstado,
        CstIbsCbs = t.CstIbsCbs,
        CClassTrib = t.CClassTrib,
        CstIS = t.CstIS,
        CClassTribIS = t.CClassTribIS,
        AliquotaIS = t.AliquotaIS,
        AliquotaIbsMunicipioDiferimento = t.AliquotaIbsMunicipioDiferimento,
        AliquotaIbsMunicipioReducao = t.AliquotaIbsMunicipioReducao
    };

    private static ProdutoDto MapParaDto(Produto p) => new()
    {
        Id = p.Id,
        CodigoInterno = p.CodigoInterno,
        CodigoBarras = p.CodigoBarras,
        Nome = p.Nome,
        CategoriaId = p.CategoriaId,
        CategoriaNome = p.Categoria?.Nome,
        MarcaId = p.MarcaId,
        MarcaNome = p.Marca?.Nome,
        QuantidadeEstoque = p.QuantidadeEstoque,
        EstoqueMinimo = p.EstoqueMinimo,
        Unidade = p.Unidade,
        TamanhoEmbalagem = p.TamanhoEmbalagem,
        PesoGramas = p.PesoGramas,
        AlturaCm = p.AlturaCm,
        LarguraCm = p.LarguraCm,
        ComprimentoCm = p.ComprimentoCm,
        ImagemProdutoPath = p.ImagemProdutoPath,
        ImagemRemovida = p.ImagemRemovida,
        Custo = p.Custo,
        PrecoVenda = p.PrecoVenda,
        PromocaoAtiva = p.PromocaoAtiva,
        PrecoPromocional = p.PrecoPromocional,
        DataValidade = p.DataValidade,
        FornecedorId = p.FornecedorId,
        FornecedorNome = p.Fornecedor?.Nome,
        Observacoes = p.Observacoes
    };
}
