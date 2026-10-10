using ImperialColors.UI.Helpers;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ImperialColors.UI.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

public class InvertBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

public class MoedaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d) return FormattingHelper.FormatarMoeda(d);
        if (value is double dbl) return FormattingHelper.FormatarMoeda((decimal)dbl);
        if (value is int i) return FormattingHelper.FormatarMoeda(i);
        return FormattingHelper.FormatarMoeda(0m);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && FormattingHelper.TryParseMoeda(s, out decimal result))
            return result;
        return 0m;
    }
}

public class DataConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime data)
            return FormattingHelper.FormatarData(data);
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DataHoraConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DateTime data)
            return FormattingHelper.FormatarDataHora(data);
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class QuantidadeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal quantidade)
            return FormattingHelper.FormatarQuantidade(quantidade);

        if (value is double quantidadeDouble)
            return FormattingHelper.FormatarQuantidade((decimal)quantidadeDouble);

        if (decimal.TryParse(value?.ToString(), NumberStyles.Number, FormattingHelper.CulturaPtBr, out var qtd))
            return FormattingHelper.FormatarQuantidade(qtd);

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class QuantidadeUnidadeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return string.Empty;

        if (values[0] is decimal quantidade)
            return FormattingHelper.FormatarQuantidadeUnidade(quantidade, values[1]?.ToString());

        if (values[0] is double quantidadeDouble)
            return FormattingHelper.FormatarQuantidadeUnidade((decimal)quantidadeDouble, values[1]?.ToString());

        if (decimal.TryParse(values[0]?.ToString(), NumberStyles.Number, FormattingHelper.CulturaPtBr, out var qtd))
            return FormattingHelper.FormatarQuantidadeUnidade(qtd, values[1]?.ToString());

        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Sigla da unidade de medida como cadastrada (UN, GL, LT...). Produto sem unidade
/// informada aparece como UN, o mesmo padrão do cadastro.</summary>
public class UnidadeMedidaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value?.ToString())
            ? "UN"
            : value!.ToString()!.Trim().ToUpperInvariant();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EstoqueStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? new SolidColorBrush(Color.FromRgb(220, 53, 69)) : new SolidColorBrush(Color.FromRgb(40, 167, 69));
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Converte um percentual (0–100, ex.: <c>LucroDiarioDto.PercentualBarra</c>) em uma
/// largura em pixels, para desenhar barras horizontais simples sem depender de biblioteca de
/// gráficos. <c>ConverterParameter</c> define a largura máxima em pixels (100% do valor);
/// usa 200 se omitido. Garante um mínimo de 3px para dias com faturamento pequeno mas maior
/// que zero não somem visualmente da barra.</summary>
public class PercentualParaLarguraConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var percentual = value switch
        {
            decimal d => (double)d,
            double db => db,
            _ => 0d
        };

        var larguraMaxima = parameter is string p && double.TryParse(p, NumberStyles.Number, CultureInfo.InvariantCulture, out var max)
            ? max : 200d;

        var largura = Math.Clamp(percentual, 0, 100) / 100d * larguraMaxima;
        return largura > 0 && largura < 3 ? 3d : largura;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Vermelho para valores negativos (ex.: lucro negativo num dia de prejuízo), verde
/// para o resto — usado nos textos de lucro do dashboard.</summary>
public class SinalParaCorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var negativo = value switch
        {
            decimal d => d < 0,
            double db => db < 0,
            _ => false
        };
        return negativo
            ? new SolidColorBrush(Color.FromRgb(220, 53, 69))
            : new SolidColorBrush(Color.FromRgb(40, 167, 69));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>Texto de situação de validade para a lista "Próximos da Validade" do Dashboard
/// (visão Estoque) — mesma lógica de <c>RelatorioService.DescreverSituacaoValidade</c> (que
/// gera o relatório em PDF/Excel), aqui como converter de UI porque é um valor curto de
/// exibição direta, não vale a pena extrair um helper compartilhado só para isso.</summary>
public class DiasParaVencerConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime dataValidade) return string.Empty;

        var dias = (dataValidade.Date - DateTime.Today).Days;
        return dias switch
        {
            < 0 => $"Vencido há {-dias} dia(s)",
            0 => "Vence hoje",
            1 => "Vence amanhã",
            _ => $"Vence em {dias} dias"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

