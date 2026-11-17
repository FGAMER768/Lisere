using System.Windows.Controls;
using Lisere.ViewModels;

namespace Lisere.Views;

public partial class EnhancePage : UserControl
{
    public EnhancePage(EnhanceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
