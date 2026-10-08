using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Application.Validation;
using ImperialColors.Domain.Exceptions;
using Xunit;

namespace ImperialColors.Application.Tests;

/// <summary>
/// Peso do produto, guardado em gramas. A unidade é fixa justamente para o campo poder ser
/// somado — o que se cobra aqui é que a conversão para quilos (como o operador confere na
/// balança, e como a NF-e pede) e a validação do que entra no campo não deixem a escala
/// escapar.
/// </summary>
public class ProdutoPesoTests
{
    [Theory]
    [InlineData(5500, "5,5 kg")]
    [InlineData(25000, "25 kg")]
    [InlineData(1000, "1 kg")]
    [InlineData(3600, "3,6 kg")]
    public void Formatar_APartirDeUmQuilo_MostraEmQuilos(int gramas, string esperado)
        => Assert.Equal(esperado, PesoProdutoHelper.Formatar(gramas));

    /// <summary>"0,8 kg" para um pote de 800 g só faz o operador conferir duas vezes se
    /// digitou certo — abaixo de um quilo a leitura natural é em gramas.</summary>
    [Theory]
    [InlineData(800, "800 g")]
    [InlineData(1, "1 g")]
    [InlineData(999, "999 g")]
    public void Formatar_AbaixoDeUmQuilo_MostraEmGramas(int gramas, string esperado)
        => Assert.Equal(esperado, PesoProdutoHelper.Formatar(gramas));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Formatar_SemPesoCadastrado_DevolveVazio(int? gramas)
        => Assert.Equal(string.Empty, PesoProdutoHelper.Formatar(gramas));

    /// <summary>A NF-e pede peso bruto/líquido em quilos — a divisão tem que ser decimal,
    /// nao inteira, senão 5500 g viraria 5 kg e a nota sairia com meio quilo a menos.</summary>
    [Fact]
    public void EmQuilos_ConverteSemPerderAFracao()
    {
        Assert.Equal(5.5m, PesoProdutoHelper.EmQuilos(5500));
        Assert.Equal(0.8m, PesoProdutoHelper.EmQuilos(800));
        Assert.Null(PesoProdutoHelper.EmQuilos(null));
    }

    [Fact]
    public void PesoFormatado_NoDto_UsaAMesmaRegraDeExibicao()
    {
        var produto = new ProdutoDto { Nome = "Tinta Acrílica 3,6L", PesoGramas = 5500 };

        Assert.Equal("5,5 kg", produto.PesoFormatado);
    }

    // ===== Validação =====

    private static CriarProdutoDto ProdutoValido(int? pesoGramas) => new()
    {
        CodigoInterno = "P001",
        Nome = "Tinta Acrílica Branca",
        CategoriaId = 1,
        MarcaId = 1,
        Unidade = "UN",
        PrecoVenda = 189.90m,
        PesoGramas = pesoGramas
    };

    /// <summary>O catálogo inteiro que já existe está sem peso — o campo não pode ser
    /// obrigatório, na edição de um produto legado. Novo cadastro usa ValidarNovo e exige dados de frete.</summary>
    [Fact]
    public void Validar_SemPeso_EhAceito()
        => ProdutoValidator.Validar(ProdutoValido(null));

    [Fact]
    public void Validar_ComPesoPlausivel_EhAceito()
        => ProdutoValidator.Validar(ProdutoValido(5500));

    /// <summary>Zero não é "sem peso", é campo preenchido errado — guardado assim, estraga
    /// qualquer soma de carga sem nunca dar erro.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validar_PesoZeroOuNegativo_Recusa(int pesoGramas)
    {
        var erro = Assert.Throws<DomainException>(() => ProdutoValidator.Validar(ProdutoValido(pesoGramas)));

        Assert.Contains("maior que zero", erro.Message);
    }

    [Fact]
    public void Validar_NaoInventaLimiteDeTransportadora()
        => ProdutoValidator.Validar(ProdutoValido(1_000_001));
    [Fact]
    public void Validar_PesoExatamenteNoLimite_EhAceito()
        => ProdutoValidator.Validar(ProdutoValido(PesoProdutoHelper.PesoMaximoGramas));
}
