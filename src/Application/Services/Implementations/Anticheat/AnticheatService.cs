using System.Threading.Channels;
using Basil.Application.Services.Contracts.Anticheat;
using Basil.Application.Services.Contracts.Multiplayer;
using Basil.Application.Services.Contracts.Sessions;
using Basil.Domain.Bancho;

namespace Basil.Application.Services.Implementations.Anticheat;

/// <summary>Judges the anticheat flags osu! clients report and announces signs of cheating.</summary>
internal sealed class AnticheatService(Lobby lobby) : IAnticheatService
{
	private readonly Channel<AnticheatEvent> _events = Channel.CreateUnbounded<AnticheatEvent>();

	/// <inheritdoc />
	public ChannelReader<AnticheatEvent> Events => _events.Reader;

	/// <inheritdoc />
	public BanchoFlags Report(BanchoConnection player, BanchoFlags flags)
	{
		var signs = flags & BanchoFlagsGroups.CheatSigns;
		if (signs == BanchoFlags.Clean) return signs;

		_events.Writer.TryWrite(new AnticheatPlayerFlagged(player, signs, lobby.RoomOf(player)));
		return signs;
	}
}
