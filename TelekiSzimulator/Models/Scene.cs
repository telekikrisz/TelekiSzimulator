using System.Text.Json.Serialization;

namespace TelekiSzimulator.Models;

public class Scene
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public int Level { get; set; }

    public bool Ending { get; set; }

    public string Text { get; set; } = "";

    public string Media { get; set; } = "";

    public List<Choice> Choices { get; set; } = [];

    [JsonIgnore]
    public bool IsStart { get; set; }

    [JsonIgnore]
    public string ListLabel
    {
        get
        {
            var title = string.IsNullOrWhiteSpace(Title) ? "(nincs cím)" : Title.ReplaceLineEndings(" ");
            if (IsStart && Ending)
                return "★■" + title;
            if (IsStart)
                return "★" + title;
            if (Ending)
                return "■" + title;

            return Level + "-" + title;
        }
    }
}
