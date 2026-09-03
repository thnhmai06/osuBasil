using Basil.Application.Sessions.Spectating;

namespace Basil.Infrastructure.Sessions;

/// <inheritdoc cref="IPlayerStatusEvents" />
/// <remarks>
///     A single nullable C# event, the same shape as <see cref="PlayerInputEvents" />. Publishing
///     invokes whichever subscribers are currently attached, synchronously and without any I/O.
/// </remarks>
public sealed class PlayerStatusEvents : IPlayerStatusEvents
{
	/// <inheritdoc />
	public event Action<int, byte[]>? StatusPublished;

	/// <inheritdoc />
	public bool HasSubscribers => StatusPublished is not null;

	/// <inheritdoc />
	public void PublishStatus(int playerId, byte[] payload)
	{
		StatusPublished?.Invoke(playerId, payload);
	}
}
