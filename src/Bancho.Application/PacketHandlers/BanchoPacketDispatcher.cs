using Bancho.Application.Sessions;
using Bancho.Protocol;

namespace Bancho.Application.PacketHandlers;

/// <summary>
/// Ported from app/state/__init__.py's packet_map ("all"/"restricted" split) + the dispatch loop
/// in app/api/domains/cho.py's bancho_handler. A single request body may contain multiple
/// packets; unhandled packet types are skipped via their declared length rather than erroring.
/// </summary>
public sealed class BanchoPacketDispatcher
{
    private readonly Dictionary<ClientPackets, IBanchoPacketHandler> _all;
    private readonly Dictionary<ClientPackets, IBanchoPacketHandler> _restrictedAllowed;

    public BanchoPacketDispatcher(IEnumerable<IBanchoPacketHandler> handlers)
    {
        var handlerList = handlers.ToList();
        _all = handlerList.ToDictionary(h => h.PacketId);
        _restrictedAllowed = handlerList.Where(h => h.AllowedWhenRestricted).ToDictionary(h => h.PacketId);
    }

    public void Dispatch(PlayerSession player, byte[] body)
    {
        var reader = new BanchoPacketReader(body);
        var handlerMap = player.Restricted ? _restrictedAllowed : _all;

        while (reader.RemainingLength > 0)
        {
            var (type, length) = reader.ReadHeader();

            if (handlerMap.TryGetValue(type, out var handler))
            {
                handler.Handle(player, reader);
            }
            else
            {
                reader.SkipRaw(length);
            }
        }
    }
}
