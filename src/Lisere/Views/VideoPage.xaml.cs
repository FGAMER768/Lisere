using System.Windows.Controls;
using Lisere.ViewModels;

namespace Lisere.Views;

public partial class VideoPage : UserControl
{
    public VideoPage(VideoViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
