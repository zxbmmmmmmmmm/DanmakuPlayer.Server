namespace DanmakuPlayer.Server.Models;

public record CurrentStatus
{
    public string? LastStatusReceived { get; set; }
    public int TotalConnectedClients { get; set; }
}