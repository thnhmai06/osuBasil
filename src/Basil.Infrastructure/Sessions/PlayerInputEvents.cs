using Basil.Application.Sessions.Spectating;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IPlayerInputEvents" />
public sealed class PlayerInputEvents : IPlayerInputEvents
{
    public event Action<int, byte[]>? InputPublished;

    public void PublishInput(int playerId, byte[] payload)
    {
        InputPublished?.Invoke(playerId, payload);
    }
}
