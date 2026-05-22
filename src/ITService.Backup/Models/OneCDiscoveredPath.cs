namespace ITService.Backup.Models;

public enum OneCPathKind
{
    File,
    Server,
    Unknown
}

public sealed class OneCDiscoveredPath
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string Source { get; init; } = "";
    public OneCPathKind Kind { get; init; }
    public bool Selected { get; set; } = true;

    public string KindLabel => Kind switch
    {
        OneCPathKind.File => "Файловая",
        OneCPathKind.Server => "Серверная",
        _ => "Другое"
    };
}
