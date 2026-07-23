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
}
