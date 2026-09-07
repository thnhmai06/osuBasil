using Basil.Server.Shared.Eventing;
using Basil.Server.Features.Spectating;

namespace Basil.Server.Features.Spectating;

/// <inheritdoc cref="IPlayerInputEvents" />
/// <remarks>
///     A single nullable C# event. Publishing invokes whichever subscribers are currently attached,
///     synchronously and without any I/O, matching the non-blocking contract shared with
///     <see cref="IMatchLiveEvents" />.
/// </remarks>
public sealed class PlayerInputEvents : IPlayerInputEvents
{
	/// <inheritdoc />
	public event Action<int, byte[]>? InputPublished;

	/// <inheritdoc />
	public bool HasSubscribers => InputPublished is not null;

	/// <inheritdoc />
	public void PublishInput(int playerId, byte[] payload)
	{
		InputPublished?.Invoke(playerId, payload);
	}
}