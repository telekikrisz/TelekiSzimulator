namespace TelekiSzimulator.Models;

public sealed class StoryCatalogItem
{
    public required string DirectoryPath { get; init; }

    public required string Title { get; init; }

    public override string ToString() => Title;
}
