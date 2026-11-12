namespace TelekiSzimulator.Models;

public class Story
{
    public string Title { get; set; } = "";

    public string Start { get; set; } = "";

    public List<Scene> Scenes { get; set; } = [];
}
