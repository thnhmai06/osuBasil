using System.Threading.Channels;
using Basil.Application.Contracts.Anticheat;
using Basil.Application.Storage.Multiplayer;
using Basil.Application.Storage.Sessions;
using Basil.Domain.Client;

namespace Basil.Application.Services.Anticheat;

/// <summary>Judges the anticheat flags osu! clients report and announces signs of cheating.</summary>
internal sealed class AnticheatService(Lobby lobby) : IAnticheatService
{
	/// <summary>The client flags that are signs of cheating.</summary>
	internal const ClientFlags CheatSigns = ClientFlags.SpeedHackDetected | ClientFlags.IncorrectModValue |
	                                        ClientFlags.MultipleOsuClients | ClientFlags.ChecksumFailure |
	                                        ClientFlags.FlashlightChecksumIncorrect |
	                                        ClientFlags.OsuExecutableChecksum | ClientFlags.MissingProcessesInList |
	                                        ClientFlags.FlashlightImageHack |
	                                        ClientFlags.SpinnerHack | ClientFlags.TransparentWindow |
	                                        ClientFlags.FastPress |
	                                        ClientFlags.RawMouseDiscrepancy | ClientFlags.RawKeyboardDiscrepancy |
	                                        ClientFlags.HqAssembly |
	                                        ClientFlags.HqFile | ClientFlags.RegistryEdits;

	private readonly Channel<AnticheatEvent> _events = Channel.CreateUnbounded<AnticheatEvent>();

	/// <inheritdoc />
	public ChannelReader<AnticheatEvent> Events => _events.Reader;

	/// <inheritdoc />
	public ClientFlags Report(BanchoConnection player, ClientFlags flags)
	{
		var signs = flags & CheatSigns;
		if (signs == ClientFlags.Clean) return signs;

		_events.Writer.TryWrite(new AnticheatPlayerFlagged(player, signs, lobby.RoomOf(player)));
		return signs;
	}
}
