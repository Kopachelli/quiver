using System.Windows.Controls;
using System.Windows.Input;

namespace SkillzWin.Views;

public partial class ItemListView : UserControl
{
    public ItemListView() => InitializeComponent();

    private void OnItemRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item) item.IsSelected = true;
    }
}
