using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ImperialColors.UI.ViewModels;

/// <summary>Uma unidade de medida (BD, GL, LA...) na lista do filtro da tela de estoque, com o
/// estado de marcada ou não.</summary>
public sealed class OpcaoUnidadeFiltro : INotifyPropertyChanged
{
    private bool _selecionada;

    public OpcaoUnidadeFiltro(string sigla, bool selecionada = false)
    {
        Sigla = sigla;
        _selecionada = selecionada;
    }

    public string Sigla { get; }

    public bool Selecionada
    {
        get => _selecionada;
        set
        {
            if (_selecionada == value)
                return;

            _selecionada = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? nome = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome));
}
