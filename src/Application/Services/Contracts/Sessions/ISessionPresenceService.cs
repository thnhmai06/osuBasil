using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Contracts.Sessions;

/// <summary>Changes what an online user shows to others: their away message and who may message them.</summary>
public interface ISessionPresenceService
{
	/// <summary>Sets or clears the away message other users see.</summary>
	/// <param name="session">The user's session.</param>
	/// <param name="message">The away message, or <see langword="null" /> or white space to clear it.</param>
	void SetAway(UserSession session, string? message);

	/// <summary>Sets whether a user accepts private messages only from their friends.</summary>
	/// <param name="session">The user's session.</param>
	/// <param name="pmPrivate"><see langword="true" /> to accept private messages only from friends.</param>
	void SetPmPrivate(UserSession session, bool pmPrivate);
}
