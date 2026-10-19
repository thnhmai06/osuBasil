using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>Changes what only an osu! client has: its presence status and the users spectating it.</summary>
public interface IBanchoConnectionService
{
	/// <summary>Spectating osu! clients.</summary>
	IBanchoSpectatorsService Spectators { get; }

	/// <summary>Records the presence status an osu! client reported.</summary>
	/// <param name="by">The client connection that reported it.</param>
	/// <param name="status">The reported status.</param>
	/// <remarks>Reporting the status the client already has, or reporting from a closed connection, does nothing.</remarks>
	void SetStatus(BanchoConnection by, PlayerStatus status);
}
