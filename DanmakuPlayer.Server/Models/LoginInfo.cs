namespace DanmakuPlayer.Server.Models;

public record LoginInfo
(
    string UserName,
    string ClientIp,
    DateTimeOffset LoginTime
);