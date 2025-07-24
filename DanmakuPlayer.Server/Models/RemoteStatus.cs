namespace DanmakuPlayer.Server.Models;

public struct RemoteStatus()
{
    public bool IsPlaying { get; set; }
    public DateTime CurrentTime { get; set; }
    public TimeSpan VideoTime { get; set; }
    public TimeSpan DanmakuDelayTime { get; set; }
    public double PlaybackRate { get; set; }
    public Dictionary<string, object> ChangedValues { get; } = [];
}
