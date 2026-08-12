using System.Globalization;
using System.Reflection;
using Basil.Application.Configurations;
using Basil.Application.Services.Content;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Irc;

/// <summary>
///     Builds the replies to an IRC client's read-only queries — the post-registration welcome
///     numerics, NAMES, TOPIC, WHO, WHOIS, MODE, MOTD, VERSION, TIME, and LUSERS.
/// </summary>
/// <remarks>
///     Membership-derived replies stay with the roster that produces them: NAMES and LIST are built
///     by <see cref="ChannelMembershipService" /> and reused here. Nothing in this class mutates
///     state — a command that would change something (setting a topic or a mode) is answered with the
///     numeric that refuses it. Instance channels such as a match or spectator room are never
///     enumerated to a user who is not already in them, since joining a channel is gated on read
///     privilege alone.
/// </remarks>
public sealed class IrcQueryService(
	IChannelRegistry channelRegistry,
	ISessionRegistry<GameSession> gameSessions,
	ISessionRegistry<IrcSession> ircSessions,
	ChannelMembershipService channelMembership,
	MotdService motd,
	IOptions<IrcOptions> options)
{
	/// <summary>The hostname half of every hostmask the gateway reports.</summary>
	private const string Host = "basil";

	/// <summary>The channel modes reported for every channel: no external messages, the topic locked.</summary>
	private const string BaseChannelModes = "+nt";

	// ponytail: "created" is when this service was first touched, not process start — close enough
	// for a 003 line, and it avoids threading a startup timestamp through DI.
	private static readonly DateTimeOffset Created = DateTimeOffset.UtcNow;

	private static readonly string Version =
		Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

	/// <summary>
	///     Builds the numerics that follow the welcome reply, describing the server, its start time,
	///     and the features it supports.
	/// </summary>
	/// <param name="nick">The nick the replies are addressed to.</param>
	/// <returns>The numerics that complete the registration handshake in wire order.</returns>
	public IEnumerable<IrcMessage> BuildWelcomeBurst(string nick)
	{
		var server = options.Value.Name;

		yield return Reply(IrcNumeric.RplYourHost, nick,
			string.Format(IrcReplies.YourHost, server, Version));
		yield return Reply(IrcNumeric.RplCreated, nick,
			string.Format(IrcReplies.ServerCreated, Created.ToString("u", CultureInfo.InvariantCulture)));
		// "-" is the conventional placeholder for a server that supports no user modes at all; an
		// empty parameter would serialize into a malformed line.
		yield return Reply(IrcNumeric.RplMyInfo, nick, server, Version, "-", "nt");
		yield return Reply(IrcNumeric.RplIsupport, nick,
			"CHANTYPES=#", "PREFIX=(o)@", $"NETWORK={server}", "CASEMAPPING=ascii",
			IrcReplies.AreSupportedByThisServer);
	}

	/// <summary>
	///     Builds the member list of one named channel, or of every channel the requester can see when
	///     no name is given.
	/// </summary>
	/// <param name="requester">The session the reply is addressed to.</param>
	/// <param name="channelName">The channel to list, or null to sweep every visible channel.</param>
	/// <returns>The numerics that form the /NAMES reply in wire order.</returns>
	public IEnumerable<IrcMessage> BuildNamesReply(UserSession requester, string? channelName)
	{
		if (channelName is null)
		{
			foreach (var channel in VisibleChannels(requester))
			foreach (var reply in channelMembership.BuildNamesReply(requester.Name, channel))
				yield return reply;

			yield break;
		}

		if (Resolve(requester, channelName) is not { } named)
		{
			yield return Reply(IrcNumeric.RplEndOfNames, requester.Name, channelName, IrcReplies.EndOfNames);
			yield break;
		}

		foreach (var reply in channelMembership.BuildNamesReply(requester.Name, named)) yield return reply;
	}

	/// <summary>
	///     Builds the topic of a channel, or the refusal when the request tries to change it.
	/// </summary>
	/// <param name="requester">The session the reply is addressed to.</param>
	/// <param name="channelName">The channel whose topic is requested.</param>
	/// <param name="newTopic">The topic the request wants to set, or null for a read.</param>
	/// <returns>The numeric that answers the request.</returns>
	public IrcMessage BuildTopicReply(UserSession requester, string channelName, string? newTopic)
	{
		if (newTopic is not null)
			return Reply(IrcNumeric.ErrChanOPrivsNeeded, requester.Name, channelName,
				IrcReplies.TopicManagedByServer);

		if (Resolve(requester, channelName) is not { } channel)
			return Reply(IrcNumeric.ErrNoSuchChannel, requester.Name, channelName, IrcReplies.NoSuchChannel);

		return string.IsNullOrEmpty(channel.Topic)
			? Reply(IrcNumeric.RplNoTopic, requester.Name, channel.Name, IrcReplies.NoTopicIsSet)
			: Reply(IrcNumeric.RplTopic, requester.Name, channel.Name, channel.Topic);
	}

	/// <summary>
	///     Builds one entry per user matching <paramref name="mask" />, which is either a channel whose
	///     members are listed or a single nick.
	/// </summary>
	/// <param name="requester">The session the reply is addressed to.</param>
	/// <param name="mask">The channel name or nick to report on.</param>
	/// <returns>The numerics that form the /WHO reply in wire order.</returns>
	public IEnumerable<IrcMessage> BuildWhoReply(UserSession requester, string mask)
	{
		if (mask.StartsWith('#'))
		{
			if (Resolve(requester, mask) is { } channel)
				foreach (var member in channel.MemberIds.Select(FindById).OfType<UserSession>())
					yield return WhoEntry(requester, channel, member);
		}
		else if (FindByName(mask) is { } single)
		{
			yield return WhoEntry(requester, null, single);
		}

		yield return Reply(IrcNumeric.RplEndOfWho, requester.Name, mask, IrcReplies.EndOfWho);
	}

	/// <summary>
	///     Builds the detail of one user: hostmask, visible channels, server, away message, and idle
	///     and sign-on times.
	/// </summary>
	/// <param name="requester">The session the reply is addressed to.</param>
	/// <param name="nick">The nick to look up.</param>
	/// <returns>The numerics that form the /WHOIS reply in wire order.</returns>
	public IEnumerable<IrcMessage> BuildWhoisReply(UserSession requester, string nick)
	{
		if (FindByName(nick) is not { } target)
		{
			yield return Reply(IrcNumeric.ErrNoSuchNick, requester.Name, nick, IrcReplies.NoSuchNickChannel);
			yield return Reply(IrcNumeric.RplEndOfWhoIs, requester.Name, nick, IrcReplies.EndOfWhoIs);
			yield break;
		}

		yield return Reply(IrcNumeric.RplWhoIsUser, requester.Name, target.Name,
			target.Id.ToString(CultureInfo.InvariantCulture), Host, "*", target.Name);

		var channels = string.Join(' ', VisibleChannels(requester)
			.Where(channel => channel.Contains(target.Id))
			.Select(channel => channel.Name));
		if (channels.Length > 0)
			yield return Reply(IrcNumeric.RplWhoIsChannels, requester.Name, target.Name, channels);

		yield return Reply(IrcNumeric.RplWhoIsServer, requester.Name, target.Name, options.Value.Name,
			IrcReplies.IrcGateway);

		if (target.AwayMessage is { } away)
			yield return Reply(IrcNumeric.RplAway, requester.Name, target.Name, away);

		var idle = (int)Math.Max(0, (DateTimeOffset.UtcNow - target.LastRecvTime).TotalSeconds);
		yield return Reply(IrcNumeric.RplWhoIsIdle, requester.Name, target.Name,
			idle.ToString(CultureInfo.InvariantCulture),
			target.LoginTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
			IrcReplies.SecondsIdleSignonTime);

		yield return Reply(IrcNumeric.RplEndOfWhoIs, requester.Name, target.Name, IrcReplies.EndOfWhoIs);
	}

	/// <summary>
	///     Builds the subset of <paramref name="nicks" /> that is currently online, under each user's
	///     stored spelling rather than the spelling the request used.
	/// </summary>
	/// <param name="requester">The session the reply is addressed to.</param>
	/// <param name="nicks">The nicknames to test, however, the request spelled them.</param>
	/// <returns>The numeric that answers the request carrying an empty list when none are online.</returns>
	public IrcMessage BuildIsonReply(UserSession requester, IEnumerable<string> nicks)
	{
		var online = nicks
			.SelectMany(nick => nick.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			.Select(FindByName)
			.OfType<UserSession>()
			.Select(session => session.Name)
			.Distinct(StringComparer.Ordinal);

		return Reply(IrcNumeric.RplIson, requester.Name, string.Join(' ', online));
	}

	/// <summary>
	///     Builds the modes set on a channel, or the refusal when the request tries to change them.
	/// </summary>
	/// <param name="requester">The session the reply is addressed to.</param>
	/// <param name="channelName">The channel whose modes are requested.</param>
	/// <param name="modeChange">The mode string the request wants to apply, or null for a read.</param>
	/// <returns>The numeric that answers the request.</returns>
	public IrcMessage BuildChannelModeReply(UserSession requester, string channelName, string? modeChange)
	{
		if (modeChange is not null)
			return Reply(IrcNumeric.ErrChanOPrivsNeeded, requester.Name, channelName,
				IrcReplies.ModesManagedByServer);

		if (Resolve(requester, channelName) is not { } channel)
			return Reply(IrcNumeric.ErrNoSuchChannel, requester.Name, channelName, IrcReplies.NoSuchChannel);

		// A channel that requires a privilege to read is not advertised to anyone who lacks it, which
		// is what the secret mode means on the wire.
		var modes = channel.ReadPrivilege == 0 ? BaseChannelModes : BaseChannelModes + "s";
		return Reply(IrcNumeric.RplChannelModeIs, requester.Name, channel.Name, modes);
	}

	/// <summary>Builds the message of the day, or the numeric reporting that none is configured.</summary>
	/// <param name="requester">The session the reply is addressed to.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>The numerics that form the /MOTD reply in wire order.</returns>
	public async Task<IReadOnlyList<IrcMessage>> BuildMotdReplyAsync(UserSession requester,
		CancellationToken cancellationToken = default)
	{
		var text = await motd.GetTextAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(text))
			return [Reply(IrcNumeric.ErrNoMotd, requester.Name, IrcReplies.NoMotd)];

		var replies = new List<IrcMessage>
		{
			Reply(IrcNumeric.RplMotdStart, requester.Name,
				string.Format(IrcReplies.MotdStart, options.Value.Name))
		};
		replies.AddRange(text.Split('\n', StringSplitOptions.TrimEntries)
			.Select(line => Reply(IrcNumeric.RplMotd, requester.Name, $"- {line}")));
		replies.Add(Reply(IrcNumeric.RplEndOfMotd, requester.Name, IrcReplies.EndOfMotd));
		return replies;
	}

	/// <summary>Builds the gateway's version reply.</summary>
	/// <param name="requester">The session the reply is addressed to.</param>
	/// <returns>The numeric that answers the request.</returns>
	public IrcMessage BuildVersionReply(UserSession requester)
	{
		return Reply(IrcNumeric.RplVersion, requester.Name, Version, options.Value.Name, IrcReplies.IrcGateway);
	}

	/// <summary>Builds the server's local time reply.</summary>
	/// <param name="requester">The session the reply is addressed to.</param>
	/// <returns>The numeric that answers the request.</returns>
	public IrcMessage BuildTimeReply(UserSession requester)
	{
		return Reply(IrcNumeric.RplTime, requester.Name, options.Value.Name,
			DateTimeOffset.Now.ToString("ddd MMM d yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture));
	}

	/// <summary>Builds the online user, connection, and channel counts.</summary>
	/// <param name="requester">The session the reply is addressed to.</param>
	/// <returns>The numerics that form the /LUSERS reply in wire order.</returns>
	public IEnumerable<IrcMessage> BuildLusersReply(UserSession requester)
	{
		var game = gameSessions.All;
		var irc = ircSessions.All;
		var accounts = game.Select(session => session.Id).Union(irc.Select(session => session.Id)).Count();
		var channels = channelRegistry.All.Count(channel => !channel.Instance);

		yield return Reply(IrcNumeric.RplLuserClient, requester.Name,
			string.Format(IrcReplies.LusersClients, accounts));
		yield return Reply(IrcNumeric.RplLuserChannels, requester.Name,
			channels.ToString(CultureInfo.InvariantCulture), IrcReplies.ChannelsFormed);
		yield return Reply(IrcNumeric.RplLuserMe, requester.Name,
			string.Format(IrcReplies.LusersMe, game.Count + irc.Count));
	}

	/// <summary>
	///     Builds one RPL_WHOREPLY entry for a user. A status prefix only means something inside a
	///     channel, so an entry not seen from one carries the away flag alone.
	/// </summary>
	private IrcMessage WhoEntry(UserSession requester, ChannelSession? channel, UserSession member)
	{
		var flags = (member.AwayMessage is null ? "H" : "G")
		            + (channel is null ? "" : channelMembership.MemberPrefix(member, channel));

		return Reply(IrcNumeric.RplWhoReply, requester.Name, channel?.Name ?? "*",
			member.Id.ToString(CultureInfo.InvariantCulture), Host, options.Value.Name, member.Name, flags,
			$"0 {member.Name}");
	}

	/// <summary>
	///     Resolves a channel a requester is allowed to be told about: one they can read, and — for an
	///     instance channel — one they are already in.
	/// </summary>
	private ChannelSession? Resolve(UserSession requester, string channelName)
	{
		var channel = channelRegistry.GetByName(channelName);
		if (channel is null || !channel.CanRead(requester.Privilege)) return null;

		return channel.Instance && !channel.Contains(requester.Id) ? null : channel;
	}

	/// <summary>Gets the channels a requester may be shown when no specific channel was named.</summary>
	private IEnumerable<ChannelSession> VisibleChannels(UserSession requester)
	{
		return channelRegistry.All
			.Where(channel => !channel.Instance && channel.CanRead(requester.Privilege))
			.OrderBy(channel => channel.Name, StringComparer.Ordinal);
	}

	private UserSession? FindByName(string name)
	{
		return (UserSession?)gameSessions.GetByName(name) ?? ircSessions.GetByName(name);
	}

	private UserSession? FindById(int userId)
	{
		return (UserSession?)gameSessions.GetByUserId(userId) ?? ircSessions.GetByUserId(userId);
	}

	private IrcMessage Reply(IrcNumeric numeric, string target, params string[] args)
	{
		return IrcMessageWriter.Numeric(options.Value.Name, numeric, target, args);
	}
}