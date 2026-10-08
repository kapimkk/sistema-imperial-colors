using Microsoft.Win32;
using System.Windows;

namespace ImperialColors.UI.Helpers;

/// <summary>Diálogos nativos isolados da lógica do formulário para testes de controles sem interação externa.</summary>
public interface IProdutoFormDialogs
{
    string? SelecionarImagem(Window owner);
    void MostrarValidacao(Window owner, string mensagem);
}

public sealed class ProdutoFormDialogs : IProdutoFormDialogs
{
    public string? SelecionarImagem(Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar imagem principal do produto",
            Filter = "Imagens JPEG e PNG|*.jpg;*.jpeg;*.png",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public void MostrarValidacao(Window owner, string mensagem)
        => MessageBox.Show(owner, mensagem, "Validação", MessageBoxButton.OK, MessageBoxImage.Warning);
}
