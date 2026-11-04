namespace GameShelf.Models;

public sealed class Session
{
    public string GameId { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
}
