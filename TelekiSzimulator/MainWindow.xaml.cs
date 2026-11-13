using System.ComponentModel;
using System.Windows;
using TelekiSzimulator.Views;

namespace TelekiSzimulator;

public partial class MainWindow : Window
{
    private bool _atHome = true;

    public MainWindow()
    {
        InitializeComponent();
        ShowHome();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_atHome)
        {
            base.OnClosing(e);
            return;
        }

        if (Host.Content is EditorView editor && !editor.TryLeave())
        {
            e.Cancel = true;
            return;
        }

        if (Host.Content is PlayerView player)
            player.Stop();

        e.Cancel = true;
        ShowHome();
    }

    private void ShowHome()
    {
        var home = new HomeView();
        home.EditorRequested += (_, _) => ShowEditor();
        home.GameRequested += (_, _) => ShowPlayer();
        Host.Content = home;
        _atHome = true;
        Title = "Teleki szimulátor";
        UseHomeSize();
    }

    private void ShowEditor()
    {
        var editor = new EditorView();
        editor.CloseRequested += (_, _) => ShowHome();
        Host.Content = editor;
        _atHome = false;
        Title = "Teleki szimulátor – szerkesztő";
        UseWorkSize();
    }

    private void ShowPlayer()
    {
        var player = new PlayerView();
        player.CloseRequested += (_, _) => ShowHome();
        player.StoryStarted += (_, _) => UseWorkSize();
        player.ReturnedToPicker += (_, _) => UsePickerSize();
        Host.Content = player;
        _atHome = false;
        Title = "Teleki szimulátor – játék";
        UsePickerSize();
    }

    private void UsePickerSize()
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;

        MinWidth = 440;
        MinHeight = 380;
        Width = 500;
        Height = 460;
        CenterOnScreen();
    }

    private void UseHomeSize()
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;

        MinWidth = 420;
        MinHeight = 320;
        Width = 480;
        Height = 420;
        CenterOnScreen();
    }

    private void UseWorkSize()
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;

        MinWidth = 1100;
        MinHeight = 680;
        Width = 1280;
        Height = 820;
        CenterOnScreen();
    }

    private void CenterOnScreen()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + Math.Max(0, (area.Height - Height) / 2);
    }
}
