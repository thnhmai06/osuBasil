using Basil.Application.Sessions.Irc;
using Basil.Domain.Users;

namespace Basil.Application.Sessions;

/// <summary>
///     Represents a real IRC connection: chat and <c>!mp</c> commands only, with no gameplay state.
///     Never occupies a multiplayer slot.
/// </summary>
/// <param name="id">The persistent id of the userSession.</param>
/// <param name="name">The userSession's username.</param>
/// <param name="token">The login token the session is keyed by.</param>
/// <param name="privilege">The server-side privilege flags granted at login.</param>
/// <param name="loginTime">The time at which the session was created.</param>
public sealed class IrcSession(int id, string name, string token, UserPrivileges privilege, DateTimeOffset loginTime)
	: UserSession(id, name, token, privilege, loginTime)
{
	/// <summary>Gets the real IRC transport this session sends and receives chat through.</summary>
	public required IIrcConnection Connection { get; init; }

	/// <inheritdoc />
	public override IIrcConnection IrcConnection => Connection;
}
