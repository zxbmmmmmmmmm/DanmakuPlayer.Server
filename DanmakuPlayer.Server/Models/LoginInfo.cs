namespace DanmakuPlayer.Server.Models;

public record LoginInfo
(
    string? UserName,
    DateTimeOffset Time,
    CurrentStatus Current
);