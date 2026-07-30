using Basil.Application.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Logging;

namespace Basil.Application.PacketHandlers.Core;

/// <summary>
///     Ported from app/state/__init__.py's packet_map ("all"/"restricted" split) + the dispatch loop
///     in app/api/domains/cho.py's bancho_handler. A single request body may contain multiple
///     packets; unhandled packet types are skipped via their declared length rather than erroring.
/// </summary>
public sealed class BanchoPacketDispatcher
{
	private readonly Dictionary<ClientPackets, IBanchoPacketHandler> _all;
	private readonly ILogger<BanchoPacketDispatcher> _logger;
	private readonly Dictionary<ClientPackets, IBanchoPacketHandler> _restrictedAllowed;

	public BanchoPacketDispatcher(IEnumerable<IBanchoPacketHandler> handlers, ILogger<BanchoPacketDispatcher> logger)
	{
		var handlerList = handlers.ToList();
		_all = handlerList.ToDictionary(h => h.PacketId);
		_restrictedAllowed = handlerList.Where(h => h.AllowedWhenRestricted).ToDictionary(h => h.PacketId);
		_logger = logger;
	}

	public async Task DispatchAsync(PlayerSession player, byte[] body, CancellationToken cancellationToken = default)
	{
		var reader = new BanchoPacketReader(body);
		var handlerMap = player.Restricted ? _restrictedAllowed : _all;

		while (reader.RemainingLength > 0)
		{
			var (type, length) = reader.ReadHeader();

			if (handlerMap.TryGetValue(type, out var handler))
			{
				var scopeProperties = new Dictionary<string, object>
				{
					["UserId"] = player.Id,
					["PacketType"] = type.ToString()
				};
				if (player.Match is { } match) scopeProperties["MatchId"] = match.DbId;

				using var _ = _logger.BeginScope(scopeProperties);
				try
				{
					await handler.HandleAsync(player, reader, cancellationToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					// A single bad packet must not take down the rest of the batch (or the connection) —
					// previously any exception here escaped with no log at all, leaving handler bugs with
					// no trace to diagnose from.
					_logger.LogError(ex, "Unhandled exception in packet handler: UserId={UserId} PacketType={PacketType}",
						player.Id, type);
				}
			}
			else
			{
				_logger.LogDebug("Unhandled packet type: UserId={UserId} PacketType={PacketType} Length={Length}",
					player.Id, type, length);
				reader.SkipRaw(length);
			}
		}
	}
}