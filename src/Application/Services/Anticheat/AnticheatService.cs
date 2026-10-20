using Basil.Application.Objects.Anticheat;
using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer;
using Basil.Application.Objects.Sessions;
using Basil.Domain.Bancho;

namespace Basil.Application.Services.Anticheat;

/// <summary>Judges the anticheat flags osu! clients report and announces signs of cheating.</summary>
internal sealed class AnticheatService(Lobby lobby, IEventPublisher<AnticheatEvent> events) : IAnticheatService
{
	/// <inheritdoc />
	public BanchoFlags Report(BanchoConnection player, BanchoFlags flags)
	{
		var signs = flags & BanchoFlagsGroups.CheatSigns;
		if (signs == BanchoFlags.Clean) return signs;

		events.Publish(new AnticheatPlayerFlagged(player, signs, lobby.RoomOf(player)));
		return signs;
	}
}