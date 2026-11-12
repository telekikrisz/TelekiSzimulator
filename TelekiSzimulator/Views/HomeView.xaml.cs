using System.Windows;
using System.Windows.Controls;

namespace TelekiSzimulator.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    public event EventHandler? EditorRequested;

    public event EventHandler? GameRequested;

    private void Editor_Click(object sender, RoutedEventArgs e) => EditorRequested?.Invoke(this, EventArgs.Empty);

    private void Game_Click(object sender, RoutedEventArgs e) => GameRequested?.Invoke(this, EventArgs.Empty);
}
