using ImperialColors.Application.DTOs;
using ImperialColors.Domain.Entities;
using ImperialColors.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// O SKU do e-commerce é exatamente o campo "Código do Produto" do cadastro de produto. O caminho:
/// <c>ProdutoFormView.xaml</c> (<c>TxtCodigoInterno</c>, rótulo "Código do Produto *")
/// → <c>ProdutoDto.CodigoInterno</c> → <c>Produto.CodigoInterno</c> → coluna <c>produtos.codigo_interno</c>.
/// O ImperialSync lê e grava por essa coluna; se alguém renomear a propriedade ou a coluna, a integração
/// com o site para de casar produtos, e estes testes acusam antes de a loja perceber.
/// </summary>
public class CodigoDoProdutoComoSkuTests
{
    private static Microsoft.EntityFrameworkCore.Metadata.IEntityType ModeloDeProduto()
    {
        // Só monta o modelo: nenhuma conexão é aberta.
        var opcoes = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=somente_modelo;Username=x;Password=x")
            .Options;
        using var contexto = new AppDbContext(opcoes);
        return contexto.Model.FindEntityType(typeof(Produto))!;
    }

    [Fact]
    public void CodigoDoProdutoGravaNaColunaCodigoInterno()
    {
        var entidade = ModeloDeProduto();

        var propriedade = entidade.FindProperty(nameof(Produto.CodigoInterno))!;

        Assert.Equal("produtos", entidade.GetTableName());
        Assert.Equal("codigo_interno", propriedade.GetColumnName());
        Assert.False(propriedade.IsNullable);
        Assert.Equal(50, propriedade.GetMaxLength());
    }

    [Fact]
    public void ACodigoInternoTemIndiceUnicoParaOSkuNaoSeRepetir()
    {
        var entidade = ModeloDeProduto();
        var propriedade = entidade.FindProperty(nameof(Produto.CodigoInterno))!;

        Assert.Contains(entidade.GetIndexes(), indice => indice.IsUnique && indice.Properties.SequenceEqual([propriedade]));
    }

    [Fact]
    public void OCodigoDeBarrasEOutraColuna_NaoESku()
    {
        var entidade = ModeloDeProduto();

        Assert.Equal("codigo_barras", entidade.FindProperty(nameof(Produto.CodigoBarras))!.GetColumnName());
        Assert.NotEqual(
            entidade.FindProperty(nameof(Produto.CodigoInterno))!.GetColumnName(),
            entidade.FindProperty(nameof(Produto.CodigoBarras))!.GetColumnName());
    }

    [Fact]
    public void OFormularioDeProdutoEntregaOCodigoDoProdutoNoMesmoCampoDoDto()
    {
        // O campo visual vira ProdutoDto.CodigoInterno; um campo renomeado quebraria o cadastro e a integração.
        var criar = typeof(CriarProdutoDto).GetProperty(nameof(CriarProdutoDto.CodigoInterno));
        var atualizar = typeof(AtualizarProdutoDto).GetProperty(nameof(AtualizarProdutoDto.CodigoInterno));

        Assert.NotNull(criar);
        Assert.NotNull(atualizar);
        Assert.Equal(typeof(string), criar.PropertyType);
        Assert.Equal(typeof(string), atualizar.PropertyType);
    }
}
