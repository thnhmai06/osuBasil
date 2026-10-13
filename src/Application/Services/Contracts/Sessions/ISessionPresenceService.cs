using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>Changes what an online user shows to others: status, away message and who may message them.</summary>
public interface ISessionPresenceService
{
	/// <summary>Records the presence status an osu! client reported.</summary>
	/// <param name="by">The client connection that reported it.</param>
	/// <param name="status">The reported status.</param>
	/// <remarks>Reporting the status the client already has, or reporting from a closed connection, does nothing.</remarks>
	void SetStatus(BanchoConnection by, PlayerStatus status);

	/// <summary>Sets or clears the away message other users see.</summary>
	/// <param name="session">The user's session.</param>
	/// <param name="message">The away message, or <see langword="null" /> or white space to clear it.</param>
	void SetAway(UserSession session, string? message);

	/// <summary>Sets whether a user accepts private messages only from their friends.</summary>
	/// <param name="session">The user's session.</param>
	/// <param name="pmPrivate"><see langword="true" /> to accept private messages only from friends.</param>
	void SetPmPrivate(UserSession session, bool pmPrivate);
}
