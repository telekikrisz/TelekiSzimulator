using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TelekiSzimulator.Models;
using TelekiSzimulator.Services;

namespace TelekiSzimulator.Views;

public partial class PlayerView : UserControl
{
    private readonly StoryRepository _repository = new();
    private readonly Dictionary<string, Scene> _scenes = new(StringComparer.Ordinal);
    private string? _directory;
    private string _startId = "";
    private bool _started;
    private bool _suppressSelection;
    private GifAnimation? _gif;
    private DispatcherTimer? _gifTimer;
    private int _gifFrame;

    public PlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => ReleasePlayer();
    }

    public event EventHandler? CloseRequested;

    public event EventHandler? StoryStarted;

    public event EventHandler? ReturnedToPicker;

    public void Stop() => ReleasePlayer();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        var stories = _repository.ListStories();
        _suppressSelection = true;
        StoryCombo.Items.Clear();
        foreach (var story in stories)
            StoryCombo.Items.Add(story);
        StoryCombo.SelectedIndex = -1;
        _suppressSelection = false;
        PickerStatus.Text = stories.Count == 0
            ? "Még nincs történet. Először a szerkesztőben lehet egyet készíteni."
            : "A lista a mentett történeteket mutatja.";
    }

    private void StoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || _started)
            return;
        if (StoryCombo.SelectedItem is not StoryCatalogItem item)
            return;

        try
        {
            var story = _repository.Load(item.DirectoryPath);
            if (story.Scenes.Count == 0 || story.Scenes.All(scene => string.IsNullOrWhiteSpace(scene.Id)))
            {
                MessageBox.Show(Window.GetWindow(this), "Ebben a történetben még nincs jelenet.", "Játék", MessageBoxButton.OK, MessageBoxImage.Information);
                ResetPicker();
                return;
            }

            _scenes.Clear();
            foreach (var scene in story.Scenes.Where(scene => !string.IsNullOrWhiteSpace(scene.Id)))
                _scenes[scene.Id] = scene;

            _directory = item.DirectoryPath;
            _startId = _scenes.ContainsKey(story.Start) ? story.Start : story.Scenes[0].Id;
            _started = true;
            Picker.Visibility = Visibility.Collapsed;
            Stage.Visibility = Visibility.Visible;
            StoryHeading.Text = string.IsNullOrWhiteSpace(story.Title) ? item.Title : story.Title;
            ShowScene(_scenes[_startId]);
            StoryStarted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "A történetet nem sikerült megnyitni.\n\n" + ex.Message, "Játék", MessageBoxButton.OK, MessageBoxImage.Error);
            ResetPicker();
        }
    }

    private void ResetPicker()
    {
        _suppressSelection = true;
        StoryCombo.SelectedIndex = -1;
        _suppressSelection = false;
    }

    private void ShowScene(Scene scene)
    {
        SceneTitle.Text = string.IsNullOrWhiteSpace(scene.Title) ? "Jelenet" : scene.Title;
        SceneText.Text = scene.Text;
        ShowMedia(scene);
        ChoiceButtons.Children.Clear();

        if (scene.Choices.Count == 0)
        {
            var back = CreateChoiceButton("Vissza a történetekhez");
            back.Click += (_, _) => ReturnToPicker();
            ChoiceButtons.Children.Add(back);
            return;
        }

        foreach (var choice in scene.Choices)
            ChoiceButtons.Children.Add(MakeButton(choice.Label, choice.Next));
    }

    private Button MakeButton(string label, string nextId)
    {
        var button = CreateChoiceButton(string.IsNullOrWhiteSpace(label) ? "Tovább" : label);
        button.Click += (_, _) => Follow(nextId);
        return button;
    }

    private static Button CreateChoiceButton(string label)
    {
        return new Button
        {
            Content = label,
            MinWidth = 180,
            MinHeight = 44,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(6, 0, 6, 0),
            FontSize = 16,
            Style = (Style)Application.Current.FindResource("PrimaryButton")
        };
    }

    private void ReturnToPicker()
    {
        ReleasePlayer();
        _started = false;
        _scenes.Clear();
        _directory = null;
        _startId = "";
        Stage.Visibility = Visibility.Collapsed;
        Picker.Visibility = Visibility.Visible;
        ResetPicker();
        ReturnedToPicker?.Invoke(this, EventArgs.Empty);
    }

    private void Follow(string nextId)
    {
        if (_scenes.TryGetValue(nextId, out var scene))
        {
            ShowScene(scene);
            return;
        }

        MessageBox.Show(Window.GetWindow(this), "Ehhez a választáshoz még nincs jelenet.", "Játék", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowMedia(Scene scene)
    {
        ReleasePlayer();
        if (_directory == null || string.IsNullOrWhiteSpace(scene.Media))
        {
            MediaNote.Text = "Nincs média.";
            MediaNote.Visibility = Visibility.Visible;
            return;
        }

        var path = Path.Combine(_directory, "media", Path.GetFileName(scene.Media));
        if (!File.Exists(path))
        {
            MediaNote.Text = "A médiafájl nem található.";
            MediaNote.Visibility = Visibility.Visible;
            return;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp")
                ShowStill(path);
            else if (extension == ".gif")
                ShowGif(path);
            else
                ShowVideo(path, extension == ".mp3");
        }
        catch (Exception ex)
        {
            ReleasePlayer();
            MediaNote.Text = ex.Message;
            MediaNote.Visibility = Visibility.Visible;
        }
    }

    private void ShowStill(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        Still.Source = image;
        Still.Visibility = Visibility.Visible;
        MediaNote.Visibility = Visibility.Collapsed;
    }

    private void ShowGif(string path)
    {
        _gif = GifAnimation.Load(path);
        _gifFrame = 0;
        Still.Source = _gif.Frames[0];
        Still.Visibility = Visibility.Visible;
        MediaNote.Visibility = Visibility.Collapsed;
        if (_gif.Frames.Count < 2)
            return;

        _gifTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_gif.Delays[0]) };
        _gifTimer.Tick += AdvanceGif;
        _gifTimer.Start();
    }

    private void AdvanceGif(object? sender, EventArgs e)
    {
        if (_gif == null || _gif.Frames.Count == 0 || _gifTimer == null)
            return;

        _gifFrame = (_gifFrame + 1) % _gif.Frames.Count;
        Still.Source = _gif.Frames[_gifFrame];
        _gifTimer.Interval = TimeSpan.FromMilliseconds(_gif.Delays[_gifFrame]);
    }

    private void ShowVideo(string path, bool audioOnly)
    {
        MediaNote.Text = audioOnly ? "Hang lejátszása folyamatban." : "";
        MediaNote.Visibility = audioOnly ? Visibility.Visible : Visibility.Collapsed;
        Player.Visibility = Visibility.Visible;
        Player.Volume = 0.6;
        Player.Source = new Uri(path, UriKind.Absolute);
        Player.Play();
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (Player.Source == null)
            return;
        if (Player.NaturalDuration.HasTimeSpan && Player.NaturalDuration.TimeSpan < TimeSpan.FromMilliseconds(200))
            return;

        Player.Position = TimeSpan.Zero;
        Player.Play();
    }

    private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        MediaNote.Text = "Ezt a fájlt nem sikerült lejátszani.";
        MediaNote.Visibility = Visibility.Visible;
    }

    private void ReleasePlayer()
    {
        if (_gifTimer != null)
        {
            _gifTimer.Stop();
            _gifTimer.Tick -= AdvanceGif;
            _gifTimer = null;
        }

        _gif = null;
        _gifFrame = 0;
        Still.Source = null;
        Still.Visibility = Visibility.Collapsed;
        Player.Stop();
        Player.Close();
        Player.Source = null;
        Player.Visibility = Visibility.Collapsed;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        ReleasePlayer();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
