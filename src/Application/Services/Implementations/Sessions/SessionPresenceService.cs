using Basil.Application.Services.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Sessions;

/// <summary>Changes what an online user shows to others: their away message and who may message them.</summary>
internal sealed class SessionPresenceService : ISessionPresenceService
{
	/// <inheritdoc />
	public void SetAway(UserSession session, string? message)
	{
		session.AwayMessage = string.IsNullOrWhiteSpace(message) ? null : message;
	}

	/// <inheritdoc />
	public void SetPmPrivate(UserSession session, bool pmPrivate)
	{
		session.PmPrivate = pmPrivate;
	}
}
