using System.Windows.Controls;

namespace RecMode.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
        ItemsList.SelectionChanged += (_, _) =>
        {
            if (ItemsList.SelectedItem is not null)
            {
                ItemsList.ScrollIntoView(ItemsList.SelectedItem);
            }
        };
    }
}
