using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Sessions;

/// <summary>Changes what only an osu! client has: its presence status and the users spectating it.</summary>
internal sealed class BanchoConnectionService(BanchoSpectatorsService spectators, SessionEventStream events)
	: IBanchoConnectionService
{
	/// <inheritdoc />
	public IBanchoSpectatorsService Spectators => spectators;

	/// <inheritdoc />
	public void SetStatus(BanchoConnection by, PlayerStatus status)
	{
		if (!by.IsOpen || Equals(by.Status, status)) return;

		by.Status = status;
		events.Emit(new UserConnectionStatusChanged(by, status));
	}
}
