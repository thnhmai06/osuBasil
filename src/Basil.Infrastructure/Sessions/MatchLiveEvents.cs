using Basil.Application.Sessions.Multiplayer;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IMatchLiveEvents" />
public sealed class MatchLiveEvents : IMatchLiveEvents
{
    public event Action<int, byte[]>? MainPublished;
    public event Action<int, string, byte[]>? PlayerScorePublished;
    public event Action<int, byte[]>? SettingsPublished;
    public event Action<int, int, byte[]>? SlotPublished;
    public event Action<int, byte[]>? LivePublished;
    public event Action<int, byte[]>? HostPublished;
    public event Action<int, byte[]>? RefsPublished;
    public event Action<int, byte[]>? BansPublished;
    public event Action<int, byte[]>? TimerPublished;
    public event Action<int, byte[]>? SlotsPublished;

    public void PublishMain(int matchDbId, byte[] payload)
    {
        MainPublished?.Invoke(matchDbId, payload);
    }

    public void PublishPlayer(int matchDbId, string playerName, byte[] payload)
    {
        PlayerScorePublished?.Invoke(matchDbId, playerName, payload);
    }

    public void PublishSettings(int matchDbId, byte[] payload)
    {
        SettingsPublished?.Invoke(matchDbId, payload);
    }

    public void PublishSlot(int matchDbId, int slotIndex, byte[] payload)
    {
        SlotPublished?.Invoke(matchDbId, slotIndex, payload);
    }

    public void PublishLive(int matchDbId, byte[] payload)
    {
        LivePublished?.Invoke(matchDbId, payload);
    }

    public void PublishHost(int matchDbId, byte[] payload)
    {
        HostPublished?.Invoke(matchDbId, payload);
    }

    public void PublishRefs(int matchDbId, byte[] payload)
    {
        RefsPublished?.Invoke(matchDbId, payload);
    }

    public void PublishBans(int matchDbId, byte[] payload)
    {
        BansPublished?.Invoke(matchDbId, payload);
    }

    public void PublishTimer(int matchDbId, byte[] payload)
    {
        TimerPublished?.Invoke(matchDbId, payload);
    }

    public void PublishSlots(int matchDbId, byte[] payload)
    {
        SlotsPublished?.Invoke(matchDbId, payload);
    }
}
