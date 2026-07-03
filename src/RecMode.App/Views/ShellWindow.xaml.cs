using System.Windows;
using RecMode.App.ViewModels;

namespace RecMode.App.Views;

public partial class ShellWindow : Window
{
    public ShellWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
