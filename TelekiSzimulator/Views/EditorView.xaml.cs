using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TelekiSzimulator.Models;
using TelekiSzimulator.Services;

namespace TelekiSzimulator.Views;

public partial class EditorView : UserControl
{
    private const string NoMediaText = "Itt jelenik meg a kiválasztott média.";

    private readonly StoryRepository _repository = new();
    private readonly ObservableCollection<Scene> _scenes = [];
    private readonly List<ChoiceRow> _choiceRows = [];

    private string? _storyDirectory;
    private string _startId = "";
    private string _savedTitle = "";
    private string? _editingId;
    private Scene? _loadedScene;
    private string? _pickedMediaPath;
    private string _savedMediaFile = "";
    private bool _loading;
    private bool _dirty;
    private bool _suppressSelection;
    private bool _suppressStoryChange;
    private GifAnimation? _gif;
    private DispatcherTimer? _gifTimer;
    private int _gifFrame;

    public EditorView()
    {
        InitializeComponent();
        SceneList.ItemsSource = _scenes;
        _scenes.CollectionChanged += (_, _) =>
            EmptyListHint.Visibility = _scenes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Loaded += OnLoaded;
        Unloaded += (_, _) => ReleasePlayer();
    }

    public event EventHandler? CloseRequested;

    public bool TryLeave()
    {
        if (_dirty && !ConfirmDiscard())
            return false;

        ReleasePlayer();
        return true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (TryLeave())
            CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private Window? HostWindow => Window.GetWindow(this);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        var latest = _repository.ListStories()
            .OrderByDescending(item => File.GetLastWriteTimeUtc(Path.Combine(item.DirectoryPath, "story.json")))
            .FirstOrDefault();

        RefreshStoryList(latest?.DirectoryPath);
        if (latest != null)
            LoadStory(latest);
        else
            SetStatus(null);
    }

    private void RefreshStoryList(string? selectDirectory)
    {
        _suppressStoryChange = true;
        StoryCombo.Items.Clear();
        StoryCatalogItem? selected = null;
        foreach (var item in _repository.ListStories())
        {
            StoryCombo.Items.Add(item);
            if (selectDirectory != null
                && string.Equals(item.DirectoryPath, selectDirectory, StringComparison.OrdinalIgnoreCase))
            {
                selected = item;
            }
        }

        StoryCombo.SelectedItem = selected;
        _suppressStoryChange = false;
    }

    private void LoadStory(StoryCatalogItem item)
    {
        Story story;
        try
        {
            story = _repository.Load(item.DirectoryPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(HostWindow, "A történetet nem sikerült megnyitni.\n\n" + ex.Message, "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _loading = true;
        _storyDirectory = item.DirectoryPath;
        _startId = story.Start;
        if (!string.IsNullOrEmpty(_startId) && story.Scenes.All(scene => scene.Id != _startId))
            _startId = story.Scenes.FirstOrDefault()?.Id ?? "";

        _savedTitle = string.IsNullOrWhiteSpace(story.Title) ? item.Title : story.Title.Trim();
        StoryNameBox.Text = _savedTitle;
        _scenes.Clear();
        foreach (var scene in story.Scenes)
            _scenes.Add(scene);

        ApplyStartFlags();
        _suppressSelection = true;
        SceneList.SelectedIndex = -1;
        _suppressSelection = false;
        DeleteSceneButton.IsEnabled = false;
        HideForm();
        FormPlaceholder.Text = _scenes.Count == 0
            ? "Ebben a történetben még nincs jelenet. Az Új jelenet gombbal vehetsz fel egyet."
            : "Válassz egy jelenetet a listából a módosításhoz, vagy vegyél fel egy újat.";
        _loading = false;
        _dirty = false;
        SetStatus("Történet megnyitva.");
    }

    private void NewStory_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !ConfirmDiscard())
            return;

        _suppressStoryChange = true;
        StoryCombo.SelectedIndex = -1;
        _suppressStoryChange = false;
        _storyDirectory = null;
        _startId = "";
        _savedTitle = "";
        _scenes.Clear();
        StoryNameBox.Text = "";
        DeleteSceneButton.IsEnabled = false;
        HideForm();
        FormPlaceholder.Text = "Új történet. Add meg a nevét felül, majd vedd fel az első jelenetet.";
        SetStatus(null);
        StoryNameBox.Focus();
    }

    private void StoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStoryChange)
            return;

        if (StoryCombo.SelectedItem is not StoryCatalogItem item)
            return;

        if (string.Equals(item.DirectoryPath, _storyDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        if (_dirty && !ConfirmDiscard())
        {
            ReselectCurrentStory();
            return;
        }

        LoadStory(item);
    }

    private void StoryNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || _storyDirectory == null)
            return;

        var title = StoryNameBox.Text.Trim();
        if (title.Length == 0)
        {
            StoryNameBox.Text = _savedTitle;
            return;
        }

        var folderName = StoryRepository.SanitizeFolderName(title);
        var sameFolder = string.Equals(Path.GetFileName(_storyDirectory), folderName, StringComparison.OrdinalIgnoreCase);
        if (title == _savedTitle && sameFolder)
            return;

        try
        {
            EnsureStoryDirectory(title);
            SaveStoryFile();
            _savedTitle = title;
            RefreshStoryList(_storyDirectory);
            SetStatus("A történet neve elmentve.");
        }
        catch (Exception ex)
        {
            StoryNameBox.Text = _savedTitle;
            MessageBox.Show(HostWindow, ex.Message, "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NewScene_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !ConfirmDiscard())
            return;

        _suppressSelection = true;
        SceneList.SelectedIndex = -1;
        _suppressSelection = false;
        DeleteSceneButton.IsEnabled = false;
        ClearForm();
    }

    private void DeleteScene_Click(object sender, RoutedEventArgs e)
    {
        if (SceneList.SelectedItem is not Scene scene || _storyDirectory == null)
            return;

        var answer = MessageBox.Show(
            HostWindow,
            "Törlöd ezt a jelenetet?\n\n" + scene.ListLabel + "\n\nA rá mutató választások célja üres lesz.",
            "Jelenet törlése",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            ReleasePlayer();
            _repository.DeleteMedia(_storyDirectory, scene.Media);
            foreach (var other in _scenes)
            {
                if (ReferenceEquals(other, scene))
                    continue;

                foreach (var choice in other.Choices)
                {
                    if (choice.Next == scene.Id)
                        choice.Next = "";
                }
            }

            _scenes.Remove(scene);
            if (_startId == scene.Id)
                _startId = _scenes.FirstOrDefault()?.Id ?? "";

            ApplyStartFlags();
            SaveStoryFile();
            _suppressSelection = true;
            SceneList.SelectedIndex = -1;
            _suppressSelection = false;
            DeleteSceneButton.IsEnabled = false;
            HideForm();
            FormPlaceholder.Text = _scenes.Count == 0
                ? "Ebben a történetben még nincs jelenet. Az Új jelenet gombbal vehetsz fel egyet."
                : "Válassz egy jelenetet a listából a módosításhoz, vagy vegyél fel egy újat.";
            SetStatus("Jelenet törölve.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(HostWindow, "A törlés nem sikerült.\n\n" + ex.Message, "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SceneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
            return;

        if (SceneList.SelectedItem is not Scene scene)
        {
            DeleteSceneButton.IsEnabled = false;
            return;
        }

        DeleteSceneButton.IsEnabled = true;
        if (ReferenceEquals(scene, _loadedScene))
            return;

        if (_dirty && !ConfirmDiscard())
        {
            _suppressSelection = true;
            SceneList.SelectedItem = _loadedScene;
            _suppressSelection = false;
            DeleteSceneButton.IsEnabled = _loadedScene != null;
            return;
        }

        LoadSceneIntoForm(scene);
    }

    private void SaveScene_Click(object sender, RoutedEventArgs e)
    {
        var title = StoryNameBox.Text.Trim();
        if (title.Length == 0)
        {
            MessageBox.Show(HostWindow, "Add meg a történet nevét felül.", "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Information);
            StoryNameBox.Focus();
            return;
        }

        var sceneTitle = SceneTitleBox.Text.Trim();
        if (sceneTitle.Length == 0)
        {
            MessageBox.Show(HostWindow, "Add meg a jelenet címét. Ez jelenik meg a bal oldali listában.", "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Information);
            SceneTitleBox.Focus();
            return;
        }

        var ending = EndingBox.IsChecked == true;
        var level = 0;
        if (!SkipLevel() && (!int.TryParse(SceneLevelBox.Text.Trim(), out level) || level < 0))
        {
            MessageBox.Show(HostWindow, "A szint egy nulla vagy nagyobb egész szám legyen, például 0, 1 vagy 2.", "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Information);
            SceneLevelBox.Focus();
            return;
        }

        var text = SceneTextBox.Text.Trim();
        if (text.Length == 0)
        {
            MessageBox.Show(HostWindow, "A jelenet szövegét add meg.", "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Information);
            SceneTextBox.Focus();
            return;
        }

        if (!TryReadChoices(out var choices))
            return;

        var isNew = _editingId == null;
        var id = _editingId ?? NextId();
        Scene? scene = null;
        var previousMedia = _savedMediaFile;

        try
        {
            EnsureStoryDirectory(title);
            var mediaFile = _savedMediaFile;
            if (!string.IsNullOrEmpty(_pickedMediaPath))
            {
                ReleasePlayer();
                mediaFile = _repository.StoreMedia(_storyDirectory!, id, _pickedMediaPath);
            }

            if (isNew)
            {
                scene = new Scene
                {
                    Id = id,
                    Title = sceneTitle,
                    Level = level,
                    Ending = ending,
                    Text = text,
                    Media = mediaFile,
                    Choices = choices
                };
                _scenes.Add(scene);
                if (string.IsNullOrEmpty(_startId))
                    _startId = id;
            }
            else
            {
                scene = _scenes.First(item => item.Id == id);
                scene.Title = sceneTitle;
                scene.Level = level;
                scene.Ending = ending;
                scene.Text = text;
                scene.Media = mediaFile;
                scene.Choices = choices;
            }

            ApplyStartFlags();
            SaveStoryFile();
            TryDeleteReplacedMedia(previousMedia, mediaFile);

            _editingId = id;
            _loadedScene = scene;
            _savedMediaFile = mediaFile;
            _pickedMediaPath = null;
            _savedTitle = title;
            _dirty = false;
            SceneIdText.Text = "Jelenet száma: " + id;
            MediaNameText.Text = string.IsNullOrEmpty(mediaFile) ? "Nincs média" : mediaFile;
            UpdateStartButton();
            RefreshAllTargetCombos();

            _suppressSelection = true;
            SceneList.SelectedItem = scene;
            _suppressSelection = false;
            DeleteSceneButton.IsEnabled = true;
            SceneList.Items.Refresh();

            var fullPath = FullMedia(mediaFile);
            if (File.Exists(fullPath))
                PlayPreview(fullPath);
            else
                StopPreview();

            RefreshStoryList(_storyDirectory);
            SetStatus(isNew ? "Új jelenet elmentve." : "Jelenet módosítva.");
        }
        catch (Exception ex)
        {
            if (isNew && scene != null)
            {
                _scenes.Remove(scene);
                if (_startId == scene.Id)
                    _startId = _scenes.FirstOrDefault()?.Id ?? "";

                ApplyStartFlags();
            }

            MessageBox.Show(HostWindow, "A mentés nem sikerült.\n\n" + ex.Message, "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetStart_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId == null || _storyDirectory == null)
        {
            MessageBox.Show(HostWindow, "Először mentsd a jelenetet. Az első mentett jelenet automatikusan a kezdő.", "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            _startId = _editingId;
            ApplyStartFlags();
            SaveStoryFile();
            RefreshAllTargetCombos();
            UpdateStartButton();
            SetStatus("Kezdőjelenet beállítva.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(HostWindow, ex.Message, "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddChoice_Click(object sender, RoutedEventArgs e)
    {
        var row = BuildChoiceRow("", "");
        _dirty = true;
        row.LabelBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Média kiválasztása",
            Filter = "Kép, animáció, videó vagy hang|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.wmv;*.mp3|Kép|*.png;*.jpg;*.jpeg;*.bmp|GIF animáció|*.gif|MP4 videó|*.mp4|MP3 hang|*.mp3|WMV videó|*.wmv",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(HostWindow) != true)
            return;

        _pickedMediaPath = dialog.FileName;
        MediaNameText.Text = Path.GetFileName(dialog.FileName);
        PlayPreview(dialog.FileName);
        _dirty = true;
    }

    private void FormField_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        _dirty = true;
    }

    private void PreviewPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (PreviewPlayer.Source == null)
            return;

        if (PreviewPlayer.NaturalDuration.HasTimeSpan
            && PreviewPlayer.NaturalDuration.TimeSpan < TimeSpan.FromMilliseconds(200))
            return;

        PreviewPlayer.Position = TimeSpan.Zero;
        PreviewPlayer.Play();
    }

    private void PreviewPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        PreviewPlaceholder.Text = "Ezt a fájlt nem sikerült lejátszani. Használj png, jpg, gif, mp4 vagy mp3 fájlt.";
        PreviewPlaceholder.Visibility = Visibility.Visible;
        var detail = e.ErrorException?.Message;
        PreviewStatus.Text = string.IsNullOrWhiteSpace(detail)
            ? "A lejátszás nem indult el."
            : detail;
    }

    private void LoadSceneIntoForm(Scene scene)
    {
        _loading = true;
        ShowForm();
        _editingId = scene.Id;
        _loadedScene = scene;
        _pickedMediaPath = null;
        _savedMediaFile = scene.Media;
        SceneIdText.Text = "Jelenet száma: " + scene.Id;
        SceneTitleBox.Text = scene.Title;
        SceneLevelBox.Text = scene.Ending ? "" : scene.Level.ToString();
        EndingBox.IsChecked = scene.Ending;
        UpdateLevelAvailability();
        SceneTextBox.Text = scene.Text;
        MediaNameText.Text = string.IsNullOrEmpty(scene.Media) ? "Nincs média" : scene.Media;
        ClearChoiceRows();
        if (scene.Choices.Count == 0)
            UpdateEndingHint();
        else
        {
            foreach (var choice in scene.Choices)
                BuildChoiceRow(choice.Label, choice.Next);
        }

        var fullPath = FullMedia(scene.Media);
        if (File.Exists(fullPath))
            PlayPreview(fullPath);
        else
            StopPreview();

        UpdateStartButton();
        _loading = false;
        _dirty = false;
    }

    private void ClearForm()
    {
        _loading = true;
        ShowForm();
        _editingId = null;
        _loadedScene = null;
        _pickedMediaPath = null;
        _savedMediaFile = "";
        _dirty = false;
        SceneIdText.Text = "Új jelenet";
        SceneTitleBox.Text = "";
        EndingBox.IsChecked = false;
        SceneLevelBox.Text = (_scenes.Count == 0 ? 0 : _scenes.Where(scene => !scene.Ending).Select(scene => scene.Level).DefaultIfEmpty(0).Max()).ToString();
        UpdateLevelAvailability();
        SceneTextBox.Text = "";
        MediaNameText.Text = "Nincs média";
        ClearChoiceRows();
        BuildChoiceRow("", "");
        StopPreview();
        UpdateStartButton();
        _loading = false;
        SceneTitleBox.Focus();
    }

    private void HideForm()
    {
        _loading = true;
        _editingId = null;
        _loadedScene = null;
        _pickedMediaPath = null;
        _savedMediaFile = "";
        _dirty = false;
        ClearChoiceRows();
        StopPreview();
        FormPanel.Visibility = Visibility.Collapsed;
        FormPlaceholder.Visibility = Visibility.Visible;
        UpdateStartButton();
        _loading = false;
    }

    private void ShowForm()
    {
        FormPlaceholder.Visibility = Visibility.Collapsed;
        FormPanel.Visibility = Visibility.Visible;
    }

    private bool TryReadChoices(out List<Choice> choices)
    {
        choices = [];
        foreach (var row in _choiceRows)
        {
            var label = row.LabelBox.Text.Trim();
            var next = (row.TargetBox.SelectedItem as ScenePick)?.Id ?? "";
            if (label.Length == 0 && next.Length == 0)
                continue;

            if (label.Length == 0)
            {
                MessageBox.Show(HostWindow, "Minden választásnak kell egy felirat.", "Szerkesztő", MessageBoxButton.OK, MessageBoxImage.Information);
                row.LabelBox.Focus();
                return false;
            }

            if (next.Length > 0 && _scenes.All(scene => scene.Id != next))
                next = "";

            choices.Add(new Choice { Label = label, Next = next });
        }

        return true;
    }

    private void EndingBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateLevelAvailability();
        if (!_loading)
            _dirty = true;
    }

    private bool SkipLevel()
    {
        var isCurrentStart = _editingId != null && _editingId == _startId;
        var willBeFirstStart = _editingId == null && string.IsNullOrEmpty(_startId);
        return EndingBox.IsChecked == true || isCurrentStart || willBeFirstStart;
    }

    private void UpdateLevelAvailability()
    {
        var skip = SkipLevel();
        SceneLevelBox.IsEnabled = !skip;
        if (skip)
            SceneLevelBox.Text = "";
    }

    private ChoiceRow BuildChoiceRow(string label, string nextId)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });

        var labelBox = new TextBox
        {
            Text = label,
            MinHeight = 34,
            Padding = new Thickness(8, 4, 8, 4),
            VerticalContentAlignment = VerticalAlignment.Center
        };

        var combo = new ComboBox
        {
            Margin = new Thickness(8, 0, 0, 0),
            MinHeight = 34,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        FillTargets(combo, nextId);

        var remove = new Button
        {
            Content = "Elvesz",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(4, 4, 4, 4)
        };

        var row = new ChoiceRow { Panel = grid, LabelBox = labelBox, TargetBox = combo };
        labelBox.TextChanged += FormField_Changed;
        combo.SelectionChanged += FormField_Changed;
        remove.Click += (_, _) => RemoveChoiceRow(row);

        Grid.SetColumn(combo, 1);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(labelBox);
        grid.Children.Add(combo);
        grid.Children.Add(remove);
        ChoicesPanel.Children.Add(grid);
        _choiceRows.Add(row);
        UpdateEndingHint();
        return row;
    }

    private void RemoveChoiceRow(ChoiceRow row)
    {
        ChoicesPanel.Children.Remove(row.Panel);
        _choiceRows.Remove(row);
        UpdateEndingHint();
        if (!_loading)
            _dirty = true;
    }

    private void ClearChoiceRows()
    {
        _choiceRows.Clear();
        ChoicesPanel.Children.Clear();
        UpdateEndingHint();
    }

    private void UpdateEndingHint()
    {
        var hasRows = _choiceRows.Count > 0;
        EndingHint.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        ChoiceHeader.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FillTargets(ComboBox combo, string selectedId)
    {
        combo.Items.Clear();
        combo.Items.Add(new ScenePick("", "— nincs —"));
        foreach (var scene in _scenes)
            combo.Items.Add(new ScenePick(scene.Id, scene.ListLabel));

        var match = combo.Items.OfType<ScenePick>().FirstOrDefault(pick => pick.Id == selectedId);
        combo.SelectedItem = match ?? combo.Items[0];
    }

    private void RefreshAllTargetCombos()
    {
        _loading = true;
        foreach (var row in _choiceRows)
        {
            var selected = (row.TargetBox.SelectedItem as ScenePick)?.Id ?? "";
            FillTargets(row.TargetBox, selected);
        }

        _loading = false;
    }

    private void ApplyStartFlags()
    {
        foreach (var scene in _scenes)
            scene.IsStart = scene.Id == _startId;

        var ordered = _scenes
            .OrderBy(scene => scene.IsStart ? 0 : scene.Ending ? 2 : 1)
            .ThenBy(scene => scene.IsStart || scene.Ending ? 0 : scene.Level)
            .ThenBy(scene => scene.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(scene => int.TryParse(scene.Id, out var number) ? number : int.MaxValue)
            .ToList();

        _suppressSelection = true;
        for (var index = 0; index < ordered.Count; index++)
        {
            var current = _scenes.IndexOf(ordered[index]);
            if (current != index)
                _scenes.Move(current, index);
        }

        _suppressSelection = false;
        SceneList.Items.Refresh();
    }

    private void UpdateStartButton()
    {
        var isCurrent = _editingId != null && _editingId == _startId;
        SetStartButton.IsEnabled = _editingId != null && _storyDirectory != null && !isCurrent;
        SetStartButton.Content = isCurrent ? "Ez a kezdőjelenet" : "Ez legyen a kezdőjelenet";
        if (IsLoaded)
            UpdateLevelAvailability();
    }

    private string NextId()
    {
        var max = 0;
        foreach (var scene in _scenes)
        {
            if (int.TryParse(scene.Id, out var number) && number > max)
                max = number;
        }

        return (max + 1).ToString();
    }

    private void EnsureStoryDirectory(string title)
    {
        var desired = Path.Combine(_repository.StoriesRoot, StoryRepository.SanitizeFolderName(title));
        if (_storyDirectory == null)
        {
            var jsonPath = Path.Combine(desired, "story.json");
            if (File.Exists(jsonPath))
                throw new InvalidOperationException("Ezen a néven már van történet. Nyisd meg a mentett történetek listájából.");

            Directory.CreateDirectory(Path.Combine(desired, "media"));
            _storyDirectory = desired;
            return;
        }

        var current = Path.GetFullPath(_storyDirectory);
        var target = Path.GetFullPath(desired);
        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
            return;

        if (Directory.Exists(desired))
        {
            if (File.Exists(Path.Combine(desired, "story.json")))
                throw new InvalidOperationException("Ezen a néven már van másik történet.");

            if (Directory.EnumerateFileSystemEntries(desired).Any())
                throw new InvalidOperationException("Ezen a néven már van egy mappa, ami nem történet.");
        }

        ReleasePlayer();
        if (Directory.Exists(desired))
            Directory.Delete(desired);

        Directory.Move(current, desired);
        _storyDirectory = desired;
    }

    private void TryDeleteReplacedMedia(string previousMedia, string mediaFile)
    {
        if (string.IsNullOrEmpty(previousMedia)
            || string.Equals(previousMedia, mediaFile, StringComparison.OrdinalIgnoreCase)
            || _storyDirectory == null)
            return;

        try
        {
            _repository.DeleteMedia(_storyDirectory, previousMedia);
        }
        catch (IOException)
        {
            // A történet már az új fájlra mutat. A régi példány akkor törölhető, ha a lejátszó elengedte.
        }
    }

    private void SaveStoryFile()
    {
        if (_storyDirectory == null)
            return;

        _repository.Save(_storyDirectory, new Story
        {
            Title = StoryNameBox.Text.Trim(),
            Start = _startId,
            Scenes = _scenes.ToList()
        });
    }

    private string FullMedia(string? fileName)
    {
        if (_storyDirectory == null || string.IsNullOrWhiteSpace(fileName))
            return "";

        return Path.Combine(_storyDirectory, "media", Path.GetFileName(fileName));
    }

    private void PlayPreview(string path)
    {
        ReleasePlayer();
        var extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp")
                ShowStillImage(path);
            else if (extension == ".gif")
                ShowGif(path);
            else
                ShowVideo(path, audioOnly: extension == ".mp3");

            PreviewStatus.Text = Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            StopPreview();
            PreviewPlaceholder.Text = "Ezt a fájlt nem sikerült megnyitni.";
            PreviewStatus.Text = ex.Message;
        }
    }

    private void ShowStillImage(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        PreviewImage.Source = image;
        PreviewImage.Visibility = Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void ShowGif(string path)
    {
        _gif = GifAnimation.Load(path);
        _gifFrame = 0;
        PreviewImage.Source = _gif.Frames[0];
        PreviewImage.Visibility = Visibility.Visible;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
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
        PreviewImage.Source = _gif.Frames[_gifFrame];
        _gifTimer.Interval = TimeSpan.FromMilliseconds(_gif.Delays[_gifFrame]);
    }

    private void ShowVideo(string path, bool audioOnly)
    {
        PreviewPlaceholder.Text = audioOnly ? "Hang lejátszása folyamatban." : NoMediaText;
        PreviewPlaceholder.Visibility = audioOnly ? Visibility.Visible : Visibility.Collapsed;
        PreviewPlayer.Visibility = Visibility.Visible;
        PreviewPlayer.Volume = 0.6;
        PreviewPlayer.Source = new Uri(path, UriKind.Absolute);
        PreviewPlayer.Play();
    }

    private void StopPreview()
    {
        ReleasePlayer();
        PreviewPlaceholder.Text = NoMediaText;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        PreviewStatus.Text = "Nincs kiválasztott média.";
    }

    private void ReleasePlayer()
    {
        // A lejátszó zárolja a fájlt. Másolás és törlés előtt el kell engedni.
        if (_gifTimer != null)
        {
            _gifTimer.Stop();
            _gifTimer.Tick -= AdvanceGif;
            _gifTimer = null;
        }

        _gif = null;
        _gifFrame = 0;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
        PreviewPlayer.Stop();
        PreviewPlayer.Close();
        PreviewPlayer.Source = null;
        PreviewPlayer.Visibility = Visibility.Collapsed;
    }

    private bool ConfirmDiscard()
    {
        var answer = MessageBox.Show(
            HostWindow,
            "A jelenet módosításai nincsenek elmentve. Elveted őket?",
            "Szerkesztő",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }

    private void ReselectCurrentStory()
    {
        _suppressStoryChange = true;
        StoryCombo.SelectedItem = StoryCombo.Items
            .OfType<StoryCatalogItem>()
            .FirstOrDefault(item => string.Equals(item.DirectoryPath, _storyDirectory, StringComparison.OrdinalIgnoreCase));
        _suppressStoryChange = false;
    }

    private void SetStatus(string? action)
    {
        var where = _storyDirectory ?? _repository.StoriesRoot;
        StatusText.Text = string.IsNullOrEmpty(action)
            ? "Történetek mappája: " + where
            : action + "   ·   " + where;
    }

    private sealed class ChoiceRow
    {
        public required Grid Panel { get; init; }

        public required TextBox LabelBox { get; init; }

        public required ComboBox TargetBox { get; init; }
    }

    private sealed class ScenePick
    {
        public ScenePick(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }
}
