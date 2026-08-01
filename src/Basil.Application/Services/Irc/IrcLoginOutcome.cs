using Basil.Application.Sessions;
using Basil.Protocol.Irc;

namespace Basil.Application.Services.Irc;

/// <summary>
///     Represents the result of an IRC PASS/NICK/USER handshake attempt.
/// </summary>
/// <param name="Success">A value that indicates whether the handshake succeeded.</param>
/// <param name="Session">
///     The authenticated session, or <see langword="null" /> when the handshake failed.
/// </param>
/// <param name="Messages">
///     The messages to emit to the connection as part of the handshake result.
/// </param>
/// <remarks>
///     A successful outcome carries the new <see cref="Session" /> and the login messages to emit
///     (welcome numerics, channel topics, and member lists). A failed outcome carries a
///     <see langword="null" /> session and a single numeric reply describing the failure.
/// </remarks>
public sealed record IrcLoginOutcome(bool Success, PlayerSession? Session, IReadOnlyList<IrcMessage> Messages)
{
	/// <summary>Builds a failed outcome carrying a single error numeric.</summary>
	/// <param name="error">The numeric reply that describes why the handshake failed.</param>
	/// <returns>A failed <see cref="IrcLoginOutcome" />.</returns>
	public static IrcLoginOutcome Failed(IrcMessage error)
	{
		return new IrcLoginOutcome(false, null, [error]);
	}

	/// <summary>Builds a successful outcome carrying the new session and its login messages.</summary>
	/// <param name="session">The authenticated session.</param>
	/// <param name="messages">The messages to emit to the connection on login.</param>
	/// <returns>A successful <see cref="IrcLoginOutcome" />.</returns>
	public static IrcLoginOutcome Ok(PlayerSession session, IReadOnlyList<IrcMessage> messages)
	{
		return new IrcLoginOutcome(true, session, messages);
	}
}