﻿namespace DanmakuPlayer.Server.Models;

public record Message(string Type, string? Data = null);
public static class MessageTypes
{
    // 有用户登录时向所有客户端广播
    public const string Login = "login";

    // 有用户断开连接时向所有客户端广播
    public const string Exit = "exit";

    // 用户改变状态时广播给其他客户端
    public const string StatusUpdate = "status_update";

    // 要求客户端发送当前状态到服务器
    public const string SendCurrentStatus = "send_current_status";
}