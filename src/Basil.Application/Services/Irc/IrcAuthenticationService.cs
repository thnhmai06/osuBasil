using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Irc;

/// <summary>
///     Authenticates a real IRC connection's PASS/NICK/USER handshake and, on success, wires up a
///     virtual <see cref="PlayerSession" /> for it.
/// </summary>
/// <remarks>
///     The session is built the same way <see cref="Basil.Application.Services.Bot.BotBootstrapService" />
///     builds one for the bot: there is no bancho socket behind it, only a session that
///     <see cref="Basil.Application.Services.Bot.ICommandDispatcher" /> and the rest of the chat core
///     can treat identically to a real osu! client. PASS is checked against the account password
///     using the same MD5-then-bcrypt flow as client login: the osu! client sends the MD5 of its
///     password as hex at login, while an IRC client sends the password in plaintext, so the
///     plaintext PASS is MD5-hashed here before verification.
/// </remarks>
public sealed class IrcAuthenticationService(
	IUserRepository users,
	IPlayerSessionRegistry sessionRegistry,
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	IOptions<IrcOptions> options,
	IPasswordHasher passwordHasher)
{
	/// <summary>
	///     Validates an IRC nick and password against the stored account and, on success, creates the
	///     session and builds the welcome message sequence for a fresh IRC login.
	/// </summary>
	/// <param name="nick">The nick the connection wants, treated as the player's username.</param>
	/// <param name="pass">The plaintext password supplied via the PASS command.</param>
	/// <param name="connection">The IRC connection being authenticated.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>
	///     An <see cref="IrcLoginOutcome" /> that either describes the failure with the numeric reply
	///     to send, or carries the new session and the welcome, topic, and names messages to emit.
	/// </returns>
	public async Task<IrcLoginOutcome> AuthenticateAsync(string nick, string pass, IIrcConnection connection,
		CancellationToken cancellationToken = default)
	{
		var user = await users.FetchByNameAsync(nick, cancellationToken);
		if (user is null)
			return IrcLoginOutcome.Failed(
				IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.ErrPasswdMismatch, nick,
					"Password incorrect"));

		var storedHash = await users.FetchPasswordHashAsync(user.Id, cancellationToken);
		if (storedHash is null)
			return IrcLoginOutcome.Failed(
				IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.ErrPasswdMismatch, nick,
					"Password incorrect"));

		var md5Hex = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(pass)));
		if (!passwordHasher.Verify(Encoding.UTF8.GetBytes(md5Hex), storedHash))
			return IrcLoginOutcome.Failed(
				IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.ErrPasswdMismatch, nick,
					"Password incorrect"));

		if (sessionRegistry.GetByName(user.Name) is not null)
			return IrcLoginOutcome.Failed(
				IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.ErrNicknameInUse, nick,
					"Nickname is already in use"));

		var loginTime = DateTimeOffset.UtcNow;
		var session = new PlayerSession(user.Id, user.Name, $"irc-{Guid.NewGuid():N}", user.Privilege, loginTime)
		{
			SilenceEnd = user.SilenceEnd,
			IrcConnection = connection
		};

		sessionRegistry.Add(session);

		var messages = new List<IrcMessage>
		{
			IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplWelcome, user.Name,
				$"Welcome to {options.Value.Name} IRC, {user.Name}")
		};

		foreach (var channel in channelRegistry.AutoJoinChannels)
		{
			if (!channel.CanRead(user.Privilege)) continue;

			channelMembership.Join(session, channel);

			if (!string.IsNullOrEmpty(channel.Topic))
				messages.Add(IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplTopic, user.Name,
					channel.Name,
					channel.Topic));

			messages.AddRange(BuildNamesReply(user.Name, channel));
		}

		return IrcLoginOutcome.Ok(session, messages);
	}

	/// <summary>
	///     Builds the RPL_NAMREPLY and RPL_ENDOFNAMES numeric pair that reports a channel's member
	///     list.
	/// </summary>
	/// <param name="requesterName">The nick the reply is addressed to.</param>
	/// <param name="channel">The channel whose members are listed.</param>
	/// <returns>The two numerics that form the channel's /NAMES reply.</returns>
	private IEnumerable<IrcMessage> BuildNamesReply(string requesterName, ChannelSession channel)
	{
		var names = channel.MemberIds
			.Select(sessionRegistry.GetById)
			.Where(member => member is not null)
			.Select(member => NamePrefix(member!) + member!.Name);

		yield return IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplNamReply, requesterName, "=",
			channel.Name,
			string.Join(' ', names));
		yield return IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplEndOfNames, requesterName,
			channel.Name,
			"End of /NAMES list");
	}

	/// <summary>
	///     Computes the IRC status prefix that precedes a channel member's nick in a /NAMES reply.
	/// </summary>
	/// <param name="member">The member whose prefix is computed.</param>
	/// <returns>
	///     The one-character status prefix, or an empty string for an unmarked member.
	/// </returns>
	/// <remarks>
	///     A member with moderator privileges is prefixed with <c>@</c>; any other member connected
	///     via an external IRC client is prefixed with <c>+</c>, matching the IRC status prefixes the
	///     official server uses.
	/// </remarks>
	private static string NamePrefix(PlayerSession member)
	{
		if ((member.Privilege & UserPrivileges.Moderator) != 0) return "@";

		return member.IrcConnection.IsExternalIrcClient ? "+" : "";
	}
}