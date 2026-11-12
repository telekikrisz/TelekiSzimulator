using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TelekiSzimulator.Models;

namespace TelekiSzimulator.Services;

public sealed class StoryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public StoryRepository()
    {
        StoriesRoot = Path.Combine(FindProjectDirectory(), "Stories");
        Directory.CreateDirectory(StoriesRoot);
    }

    public string StoriesRoot { get; }

    public IReadOnlyList<StoryCatalogItem> ListStories()
    {
        if (!Directory.Exists(StoriesRoot))
            return [];

        var result = new List<StoryCatalogItem>();
        foreach (var directory in Directory.GetDirectories(StoriesRoot))
        {
            var jsonPath = Path.Combine(directory, "story.json");
            if (!File.Exists(jsonPath))
                continue;

            var title = Path.GetFileName(directory);
            try
            {
                var story = Load(directory);
                if (!string.IsNullOrWhiteSpace(story.Title))
                    title = story.Title.Trim();
            }
            catch (JsonException)
            {
                title += " (sérült fájl)";
            }

            result.Add(new StoryCatalogItem
            {
                DirectoryPath = directory,
                Title = title
            });
        }

        result.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Title, b.Title));
        return result;
    }

    public Story Load(string storyDirectory)
    {
        var path = Path.Combine(storyDirectory, "story.json");
        var json = File.ReadAllText(path);
        var story = JsonSerializer.Deserialize<Story>(json, JsonOptions) ?? new Story();
        story.Title ??= "";
        story.Start ??= "";
        story.Scenes ??= [];

        foreach (var scene in story.Scenes)
        {
            scene.Id ??= "";
            scene.Title ??= "";
            scene.Text ??= "";
            scene.Media = Path.GetFileName(scene.Media ?? "");
            scene.Choices ??= [];
            foreach (var choice in scene.Choices)
            {
                choice.Label ??= "";
                choice.Next ??= "";
            }
        }

        return story;
    }

    public void Save(string storyDirectory, Story story)
    {
        Directory.CreateDirectory(storyDirectory);
        var json = JsonSerializer.Serialize(story, JsonOptions);
        var path = Path.Combine(storyDirectory, "story.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }

    public string StoreMedia(string storyDirectory, string sceneId, string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".mp4" or ".wmv" or ".mp3"))
            throw new InvalidOperationException("Csak png, jpg, gif, bmp, mp4, wmv vagy mp3 fájl menthető.");

        var mediaDirectory = Path.Combine(storyDirectory, "media");
        Directory.CreateDirectory(mediaDirectory);

        var fileName = sceneId + extension;
        var destination = Path.GetFullPath(Path.Combine(mediaDirectory, fileName));
        var source = Path.GetFullPath(sourcePath);
        if (!string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            File.Copy(source, destination, overwrite: true);

        return fileName;
    }

    public void DeleteMedia(string storyDirectory, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        var path = Path.Combine(storyDirectory, "media", Path.GetFileName(fileName));
        if (File.Exists(path))
            File.Delete(path);
    }

    public static string SanitizeFolderName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        cleaned = cleaned.Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(cleaned) ? "tortenet" : cleaned;
    }

    private static string FindProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (directory.EnumerateFiles("*.csproj").Any())
                return directory.FullName;

            directory = directory.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
