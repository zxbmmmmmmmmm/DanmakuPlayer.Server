using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;

namespace DanmakuPlayer.Server.Controllers;

public class SyncController(ILogger<SyncController> logger) : ControllerBase
{
    private static readonly List<WebSocket> Sockets = [];
    private static string? _currentStatus;
    private static readonly Lock Lock = new Lock();
    private ILogger _logger = logger;

    [HttpGet]
    [Route("sync")]
    public async Task Sync()
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            lock (Lock)
            {
                Sockets.Add(webSocket);
            }
            _logger.LogInformation("Websocket connected from {RemoteIp}", HttpContext.Connection.RemoteIpAddress?.ToString());

            await HandleWebSocketConnection(webSocket);
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }

    [HttpGet]
    [Route("current")]
    public string? Get()
    {
        _logger.LogInformation("Get current status from {RemoteIp}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return _currentStatus;
    }
   

    [HttpGet]
    [Route("test")]
    public string Test() => "test";

    private async Task HandleWebSocketConnection(WebSocket webSocket)
    {
        var buffer = new byte[1024 * 4];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                    break;
                }

                // 处理接收到的消息
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                _currentStatus = message;
                _logger.LogInformation("Message received from {RemoteIp}, Message:{Message}", 
                    HttpContext.Connection.RemoteIpAddress?.ToString(), message);

                await BroadcastMessageAsync(message, webSocket);
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            // 客户端意外断开
        }
        finally
        {
            lock (Lock)
            {
                Sockets.Remove(webSocket);
                if (Sockets.Count == 0)
                {
                    _currentStatus = null;
                }
            }
            webSocket.Dispose();
            _logger.LogInformation("Disconnected from {RemoteIp}", HttpContext.Connection.RemoteIpAddress?.ToString());

        }
    }

    private async Task BroadcastMessageAsync(string message,WebSocket currentWebSocket)
    {
        var buffer = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(buffer);
        List<WebSocket> activeSockets;
        // 获取所有活跃连接的安全副本
        lock (Lock)
        { 
            activeSockets = Sockets
                .Where(s => s.State == WebSocketState.Open)
                .ToList();
        }


        // 并行发送但限制并发数
        var sendTasks = new List<Task>();
        var throttler = new SemaphoreSlim(initialCount: 10); // 限制并发发送数

        foreach (var socket in activeSockets)
        {
            await throttler.WaitAsync();
            sendTasks.Add(Task.Run(async () =>
            {
                try
                {
                    if (socket.State == WebSocketState.Open && socket != currentWebSocket)
                    {
                        await socket.SendAsync(segment,
                            WebSocketMessageType.Text,
                            true,
                            CancellationToken.None);
                    }
                }
                catch (WebSocketException)
                {
                    // 发送失败忽略
                }
                finally
                {
                    throttler.Release();
                }
            }));
        }

        await Task.WhenAll(sendTasks);
    }

}

public struct RemoteStatus()
{
    public bool IsPlaying { get; set; }
    public DateTime CurrentTime { get; set; }
    public TimeSpan VideoTime { get; set; }
    public TimeSpan DanmakuDelayTime { get; set; }
    public double PlaybackRate { get; set; }
    public Dictionary<string, object> ChangedValues { get; } = [];
}
