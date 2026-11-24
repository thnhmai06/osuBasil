using Basil.Domain.Sessions;

namespace Basil.Host.Irc.Support;

/// <summary>The fixed values of the IRC host that are not wording: the network name and the code of a rate-limit failure.</summary>
public static class IrcReplies
{
	public const string Network = "Basil";

	public const string RateLimitedCode = "RATE_LIMITED";

	/// <summary>Gets the reply that words why a session ended.</summary>
	public static IrcReply Closing(SessionEndReason reason)
	{
		return reason switch
		{
			SessionEndReason.LoggedOut => IrcReply.ClosingQuit,
			SessionEndReason.ConnectionLost => IrcReply.ClosingConnectionLost,
			SessionEndReason.TimedOut => IrcReply.ClosingTimedOut,
			SessionEndReason.Replaced => IrcReply.ClosingReplaced,
			SessionEndReason.Revoked or SessionEndReason.CredentialsChanged => IrcReply.ClosingRevoked,
			SessionEndReason.Deleted => IrcReply.ClosingDeleted,
			_ => IrcReply.ClosingOther
		};
	}
}
