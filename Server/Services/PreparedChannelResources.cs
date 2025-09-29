using WicsPlatform.Server.Middleware;

namespace WicsPlatform.Server.Services;

/// <summary>
/// Wrapper model for prepared channel resources (speakers + ordered playlist).
/// Media / TTS lists are derived from OrderedPlaylist for a single source of truth.
/// </summary>
public sealed class PreparedChannelResources
{
    public List<SpeakerInfo> Speakers { get; }
    public List<PlaylistEntry> OrderedPlaylist { get; }

    // Derived collections (re-computed on each access to reflect playlist state)
    public IReadOnlyList<WebSocketMiddleware.MediaInfo> Media => OrderedPlaylist
        .Where(p => p.IsMedia && p.Media != null)
        .Select(p => p.Media!)
        .ToList();

    public IReadOnlyList<WebSocketMiddleware.TtsInfo> Tts => OrderedPlaylist
        .Where(p => !p.IsMedia && p.Tts != null)
        .Select(p => p.Tts!)
        .ToList();

    public PreparedChannelResources(List<SpeakerInfo> speakers, List<PlaylistEntry>? ordered)
    {
        Speakers = speakers ?? new List<SpeakerInfo>();
        OrderedPlaylist = ordered ?? new List<PlaylistEntry>();
    }
}

public sealed class PlaylistEntry
{
    public bool IsMedia { get; }
    public ulong Id { get; }
    public WebSocketMiddleware.MediaInfo? Media { get; }
    public WebSocketMiddleware.TtsInfo? Tts { get; }
    public PlaylistEntry(WebSocketMiddleware.MediaInfo media)
    {
        IsMedia = true; Id = media.Id; Media = media; Tts = null;
    }
    public PlaylistEntry(WebSocketMiddleware.TtsInfo tts)
    {
        IsMedia = false; Id = tts.Id; Media = null; Tts = tts;
    }
}
