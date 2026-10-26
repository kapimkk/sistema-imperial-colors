using ImperialColors.Application.DTOs;
using Xunit;

namespace ImperialColors.Application.Tests;

public class TributacaoPreenchidaTests
{
    public static IEnumerable<object[]> CamposFiscais()
    {
        foreach (var tipo in new[] { typeof(TributacaoProdutoDto), typeof(TributacaoCategoriaDto) })
        foreach (var campo in tipo.GetProperties().Where(p => p.CanWrite && p.Name is not ("ProdutoId" or "CategoriaId")))
        {
            var tipoValor = Nullable.GetUnderlyingType(campo.PropertyType) ?? campo.PropertyType;
            object valor = tipoValor == typeof(string) ? "001" :
                tipoValor == typeof(decimal) ? 12.34m : Enum.ToObject(tipoValor, 0);
            yield return new object[] { tipo, campo.Name, valor };
            if (tipoValor == typeof(decimal))
                yield return new object[] { tipo, campo.Name, 0m };
        }
    }

    [Theory]
    [MemberData(nameof(CamposFiscais))]
    public void CampoFiscalIsolado_ContaComoTributacaoPreenchida(Type tipo, string campo, object valor)
    {
        var dto = Activator.CreateInstance(tipo)!;
        tipo.GetProperty(campo)!.SetValue(dto, valor);

        Assert.True((bool)tipo.GetProperty("Preenchida")!.GetValue(dto)!);
    }

    [Fact]
    public void ApenasIdentificadorOuTextoEmBranco_NaoContaComoTributacao()
    {
        var produto = new TributacaoProdutoDto { ProdutoId = 1 };
        var categoria = new TributacaoCategoriaDto { CategoriaId = 1 };
        Assert.False(produto.Preenchida);
        Assert.False(categoria.Preenchida);

        foreach (var dto in new object[] { produto, categoria })
        {
            foreach (var campo in dto.GetType().GetProperties().Where(p => p.CanWrite && p.PropertyType == typeof(string)))
                campo.SetValue(dto, "  ");
            Assert.False((bool)dto.GetType().GetProperty("Preenchida")!.GetValue(dto)!);
        }
    }

}
