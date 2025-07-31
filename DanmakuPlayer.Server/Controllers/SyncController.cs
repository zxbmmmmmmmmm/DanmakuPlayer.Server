using DanmakuPlayer.Server.Models;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DanmakuPlayer.Server.Controllers;

public class SyncController(ILogger<SyncController> logger) : ControllerBase
{
    private static readonly List<WebSocket> Sockets = [];
    private static readonly CurrentStatus CurrentStatus = new();
    private static readonly Lock Lock = new Lock();
    private readonly ILogger _logger = logger;
    private const int ReceiveBufferSize = 4096;

    [HttpGet]
    [Route("sync")]
    [HttpGet("{userName}")]
    public async Task Sync(string userName)
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {


            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            WebSocket? socketToSendMessage = null;
            lock (Lock)
            {
                if(Sockets.Count != 0)
                {
                    socketToSendMessage = Sockets.FirstOrDefault(s => s.State == WebSocketState.Open);
                }
                Sockets.Add(webSocket);
                CurrentStatus.TotalConnectedClients++;
            }
            _logger.LogInformation("Websocket connected from {RemoteIp}", HttpContext.Connection.RemoteIpAddress?.ToString());

            var loginInfo = new LoginInfo(
                UserName: userName,
                Time: DateTimeOffset.Now,
                Current: CurrentStatus
            );
            await BroadcastMessageAsync(new Message(MessageTypes.Login, JsonSerializer.Serialize(loginInfo)));
            if(socketToSendMessage is not null)
            {
                await SendMessageAsync(new Message(MessageTypes.SendCurrentStatus), socketToSendMessage);
            }
            await HandleWebSocketConnection(webSocket);
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }

    [HttpGet]
    [Route("current")]
    public CurrentStatus? Get()
    {
        _logger.LogInformation("Get current status from {RemoteIp}", HttpContext.Connection.RemoteIpAddress?.ToString());
        return CurrentStatus;
    }
   

    [HttpGet]
    [Route("test")]
    public string Test() => "test";

    private async Task HandleWebSocketConnection(WebSocket webSocket)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
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
                var messageReceived = Encoding.UTF8.GetString(buffer, 0, result.Count);

                CurrentStatus.LastStatusReceived = messageReceived;
                _logger.LogInformation("Message received from {RemoteIp}, Message:{Message}",
                    HttpContext.Connection.RemoteIpAddress?.ToString(), messageReceived);

                await BroadcastMessageAsync(new Message(MessageTypes.StatusUpdate, messageReceived), webSocket);

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
                CurrentStatus.TotalConnectedClients--;
            }
            webSocket.Dispose();
            var info = new LoginInfo(
                null,
                Time: DateTimeOffset.Now,
                Current: CurrentStatus
            );
            await BroadcastMessageAsync(new Message(MessageTypes.StatusUpdate, JsonSerializer.Serialize(info)));
            _logger.LogInformation("Disconnected from {RemoteIp}", HttpContext.Connection.RemoteIpAddress?.ToString());
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task BroadcastMessageAsync(Message message, WebSocket? currentWebSocket = null)
    {
        var buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
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


    private async Task SendMessageAsync(Message message, WebSocket targetWebSocket)
    {
        var buffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        var segment = new ArraySegment<byte>(buffer);

        if (targetWebSocket.State == WebSocketState.Open)
        {
            await targetWebSocket.SendAsync(segment,
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }
    }
}