using Basil.Application.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.Packets;

/// <summary>
///     Dispatches the packets in a client request body to their registered
///     <see cref="IPacketHandler" /> implementations.
/// </summary>
/// <remarks>
///     A single request body may contain multiple packets, so dispatch iterating until the body is
///     exhausted. Which packet types are handled at all depends on whether the sending userSession is
///     restricted: restricted players only reach the subset of handlers whose
///     <see cref="IPacketHandler.AllowedWhenRestricted" /> flag is set. Packet types without a
///     handler in the active set are skipped by advancing past their declared payload length, so one
///     unknown packet cannot invalidate the rest of the batch.
/// </remarks>
public sealed class PacketDispatcher
{
	private readonly Dictionary<ClientPackets, IPacketHandler> _all;
	private readonly ILogger<PacketDispatcher> _logger;
	private readonly Dictionary<ClientPackets, IPacketHandler> _restrictedAllowed;

	/// <summary>
	///     Builds the full and restricted-allowed handler maps from the supplied handlers.
	/// </summary>
	/// <param name="handlers">The packet handlers registered for this server.</param>
	/// <param name="logger">The logger used for handler failures and skipped packet types.</param>
	public PacketDispatcher(IEnumerable<IPacketHandler> handlers, ILogger<PacketDispatcher> logger)
	{
		var handlerList = handlers.ToList();
		_all = handlerList.ToDictionary(h => h.PacketId);
		_restrictedAllowed =
			handlerList.Where(h => h.AllowedWhenRestricted).ToDictionary(h => h.PacketId);
		_logger = logger;
	}

	/// <summary>
	///     Reads and dispatches every packet in <paramref name="body" /> for the given userSession.
	/// </summary>
	/// <remarks>
	///     Each handled packet runs within a logging scope carrying the user id, the packet type, and
	///     the userSession's current match id when present. An exception thrown by a handler is logged and
	///     swallowed so that a single bad packet does not take down the rest of the batch or the
	///     connection; only an <see cref="OperationCanceledException" /> propagates. Unhandled packet
	///     types are logged at debug level and skipped.
	/// </remarks>
	/// <param name="userSession">The userSession session whose request body is being dispatched.</param>
	/// <param name="body">The raw request body containing one or more Bancho packets.</param>
	/// <param name="cancellationToken">The token used to cancel the dispatch.</param>
	/// <returns>A task that completes once every packet in the body has been dispatched.</returns>
	public async Task DispatchAsync(UserSession userSession, byte[] body, CancellationToken cancellationToken = default)
	{
		var reader = new PacketReader(body);
		var handlerMap = userSession.Restricted ? _restrictedAllowed : _all;

		while (reader.RemainingLength > 0)
		{
			var (type, length) = reader.ReadHeader();

			if (handlerMap.TryGetValue(type, out var handler))
			{
				var scopeProperties = new Dictionary<string, object>
				{
					["UserId"] = userSession.Id,
					["PacketType"] = type.ToString()
				};
				if (userSession.Match is { } match) scopeProperties["MatchId"] = match.DbId;

				using var _ = _logger.BeginScope(scopeProperties);
				try
				{
					await handler.HandleAsync(userSession, reader, cancellationToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// A single bad packet must not take down the rest of the batch (or the connection) —
					// previously any exception here escaped with no log at all, leaving handler bugs with
					// no trace to diagnose from.
					_logger.LogError(ex,
						"Unhandled exception in packet handler: UserId={UserId} PacketType={PacketType}",
						userSession.Id, type);
				}
			}
			else
			{
				_logger.LogDebug("Unhandled packet type: UserId={UserId} PacketType={PacketType} Length={Length}",
					userSession.Id, type, length);
				reader.SkipRaw(length);
			}
		}
	}
}