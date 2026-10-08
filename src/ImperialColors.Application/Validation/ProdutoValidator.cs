using ImperialColors.Application.DTOs;
using ImperialColors.Application.Helpers;
using ImperialColors.Domain.Constants;
using ImperialColors.Domain.Exceptions;

namespace ImperialColors.Application.Validation;

public static class ProdutoValidator
{
    public static void Validar(CriarProdutoDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.CodigoInterno))
            throw new DomainException("Código interno é obrigatório.");

        if (string.IsNullOrWhiteSpace(dto.Nome))
            throw new DomainException("Nome do produto é obrigatório.");

        if (!dto.CategoriaId.HasValue || dto.CategoriaId.Value <= 0)
            throw new DomainException("Selecione uma Categoria e uma Marca válidas.");

        if (!dto.MarcaId.HasValue || dto.MarcaId.Value <= 0)
            throw new DomainException("Selecione uma Categoria e uma Marca válidas.");

        if (dto.PrecoVenda <= 0)
            throw new DomainException("Preço de venda deve ser maior que zero.");

        if (dto.Custo is <= 0)
            throw new DomainException("Preço de custo, quando informado, deve ser maior que zero.");

        if (dto.QuantidadeEstoque < 0)
            throw new DomainException("Quantidade em estoque não pode ser negativa.");

        if (dto.EstoqueMinimo < 0)
            throw new DomainException("Estoque mínimo não pode ser negativo.");

        if (string.IsNullOrWhiteSpace(dto.Unidade))
            throw new DomainException("Unidade de medida é obrigatória.");

        if (!UnidadesMedida.EhValida(dto.Unidade))
            throw new DomainException(
                $"Unidade de medida inválida. Use: {string.Join(", ", UnidadesMedida.Todas)}.");

        ValidarPeso(dto.PesoGramas);
        ValidarDimensao(dto.AlturaCm, "Altura");
        ValidarDimensao(dto.LarguraCm, "Largura");
        ValidarDimensao(dto.ComprimentoCm, "Comprimento");
        if (dto.Observacoes?.Length > 10_000)
            throw new DomainException("A descrição deve ter no máximo 10.000 caracteres.");
        if (dto.RemoverImagem && dto.AlterarImagem)
            throw new DomainException("Selecione a alteração ou remoção da imagem, não ambas.");
        ValidarPromocao(dto.PromocaoAtiva, dto.PrecoPromocional, dto.PrecoVenda);
    }

    /// <summary>Legado pode continuar nulo. Valor informado é positivo; transportadoras definem seus limites.</summary>
    private static void ValidarPeso(int? pesoGramas)
    {
        if (pesoGramas is null)
            return;

        if (pesoGramas <= 0)
            throw new DomainException("Peso, quando informado, deve ser maior que zero.");

    }

    public static void ValidarNovo(CriarProdutoDto dto)
    {
        Validar(dto);
        if (!dto.PesoGramas.HasValue || !dto.AlturaCm.HasValue || !dto.LarguraCm.HasValue || !dto.ComprimentoCm.HasValue)
            throw new DomainException("Informe peso, altura, largura e comprimento para cadastrar um novo produto.");
    }

    private static void ValidarDimensao(decimal? valor, string nome)
    {
        if (valor is null) return;
        if (valor <= 0)
            throw new DomainException($"{nome}, quando informada, deve ser maior que zero (cm).");
        if (valor >= 100_000_000m || decimal.Round(valor.Value, 2) != valor.Value)
            throw new DomainException($"{nome} deve ter no máximo duas casas decimais e caber no campo em cm.");
    }

    private static void ValidarPromocao(bool promocaoAtiva, decimal? precoPromocional, decimal precoVenda)
    {
        if (!promocaoAtiva)
            return;

        if (!precoPromocional.HasValue || precoPromocional.Value <= 0)
            throw new DomainException("Informe o preço promocional quando o modo promocional estiver ativo.");

        if (precoPromocional.Value > precoVenda)
            throw new DomainException("O preço promocional não pode ser maior que o preço de venda padrão.");
    }

    public static void Validar(AtualizarProdutoDto dto) => Validar((CriarProdutoDto)dto);
}
