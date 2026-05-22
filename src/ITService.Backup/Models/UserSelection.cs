namespace ITService.Backup.Models;

public sealed class UserSelection
{
    public List<string> Sql { get; set; } = [];
    public List<string> Folders { get; set; } = [];
    public List<string> Files { get; set; } = [];
}
