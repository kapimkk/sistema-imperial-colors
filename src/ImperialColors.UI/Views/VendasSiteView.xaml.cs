using ImperialColors.UI.ViewModels;
using System.Windows.Controls;

namespace ImperialColors.UI.Views;

/// <summary>
/// "Vendas Site": as vendas criadas pelo ImperialSync a partir de pedidos pagos no e-commerce e o
/// botão "Sincronizar com o Site". Toda a lógica está em <see cref="VendasSiteViewModel"/>.
/// </summary>
public partial class VendasSiteView : UserControl
{
    public VendasSiteView(VendasSiteViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
