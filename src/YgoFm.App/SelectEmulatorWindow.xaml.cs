using System.Windows;
using YgoFm.Vision;

namespace YgoFm.App;

/// <summary>Modal picker over <see cref="WindowFinder"/>'s list, so the user tells us which
/// window is the emulator. Deliberately generic — any window can be chosen, per the
/// emulator-agnostic constraint.</summary>
public partial class SelectEmulatorWindow : Window
{
    public WindowInfo? Chosen { get; private set; }

    public SelectEmulatorWindow()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        var selected = (WindowInfo?)WindowList.SelectedItem;
        WindowList.ItemsSource = WindowFinder.Visible();
        if (selected is not null)
            WindowList.SelectedItem = ((List<WindowInfo>)WindowList.ItemsSource)
                .FirstOrDefault(w => w.Handle == selected.Handle);
        else if (WindowList.Items.Count > 0)
            WindowList.SelectedIndex = 0;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is not WindowInfo window)
        {
            MessageBox.Show(this, "Escolha uma janela na lista primeiro.", "Nenhuma janela selecionada",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Chosen = window;
        DialogResult = true;
    }
}
