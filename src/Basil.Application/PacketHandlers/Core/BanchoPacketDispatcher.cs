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
				await handler.HandleAsync(player, reader, cancellationToken);
			}
			else
			{
				reader.SkipRaw(length);
			}
		}
	}
}