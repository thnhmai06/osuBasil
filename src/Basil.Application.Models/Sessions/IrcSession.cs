using Basil.Domain.Users;

namespace Basil.Application.Models.Sessions;

/// <summary>A real IRC connection's session: chat and commands only, with no gameplay state.</summary>
public sealed class IrcSession(User user, DateTimeOffset loginTime) : UserSession
{
	/// <inheritdoc />
	public override User User => user;

	/// <inheritdoc />
	public override DateTimeOffset LoginTime => loginTime;
}
