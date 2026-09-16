using Basil.Application.Irc;
using Basil.Protocol.Irc;

namespace Basil.Infrastructure.Irc;

/// <summary>
///     Builds the RPL_NAMREPLY and RPL_ENDOFNAMES numeric pair that reports a channel's member list.
///     The single place that formats a NAMES reply, shared by an unsolicited join echo and an
///     explicit /NAMES query.
/// </summary>
public static class IrcNamesReply
{
	/// <param name="serverName">The gateway's name, reported as the numeric's prefix.</param>
	/// <param name="requesterName">The nick the reply is addressed to.</param>
	/// <param name="channelName">The channel whose members are listed.</param>
	/// <param name="roster">The channel's member names, already prefixed by standing.</param>
	/// <returns>The two numerics that form the channel's /NAMES reply.</returns>
	public static IEnumerable<IrcMessage> Build(string serverName, string requesterName, string channelName,
		IReadOnlyList<string> roster)
	{
		yield return IrcMessageWriter.Numeric(serverName, IrcNumeric.RplNamReply, requesterName, "=",
			channelName, string.Join(' ', roster));
		yield return IrcMessageWriter.Numeric(serverName, IrcNumeric.RplEndOfNames, requesterName,
			channelName, IrcReplies.EndOfNames);
	}
}
