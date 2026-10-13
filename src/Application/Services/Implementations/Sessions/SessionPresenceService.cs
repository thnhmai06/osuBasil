using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Sessions;

namespace Basil.Application.Services.Implementations.Sessions;

/// <summary>Changes what an online user shows to others: status, away message and who may message them.</summary>
internal sealed class SessionPresenceService(SessionEventStream events) : ISessionPresenceService
{
	/// <inheritdoc />
	public void SetStatus(BanchoConnection by, PlayerStatus status)
	{
		if (!by.IsOpen || Equals(by.Status, status)) return;

		by.Status = status;
		events.Emit(new UserConnectionStatusChanged(by, status));
	}

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
