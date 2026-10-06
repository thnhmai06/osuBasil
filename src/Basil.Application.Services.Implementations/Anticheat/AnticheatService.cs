using System.Threading.Channels;
using Basil.Application.Services.Contracts.Anticheat;
using Basil.Application.Storage.Contracts.Multiplayer;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Client;

namespace Basil.Application.Services.Implementations.Anticheat;

/// <summary>Judges the anticheat flags osu! clients report and announces signs of cheating.</summary>
internal sealed class AnticheatService(ILobby lobby) : IAnticheatService
{
	private readonly Channel<AnticheatEvent> _events = Channel.CreateUnbounded<AnticheatEvent>();

	/// <inheritdoc />
	public ChannelReader<AnticheatEvent> Events => _events.Reader;

	/// <inheritdoc />
	public ClientFlags Report(BanchoConnection player, ClientFlags flags)
	{
		var signs = flags & ClientFlags.CheatSigns;
		if (signs == ClientFlags.Clean) return signs;

		_events.Writer.TryWrite(new AnticheatPlayerFlagged(player, signs, lobby.RoomOf(player)));
		return signs;
	}
}