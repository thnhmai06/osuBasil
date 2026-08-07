using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Bot;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configurations;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Options;

namespace Basil.Application.Services.Irc;

/// <summary>
///     Authenticates a real IRC connection's PASS/NICK/USER handshake and, on success, wires up an
///     <see cref="IrcSession" /> for it.
/// </summary>
/// <remarks>
///     The session is chat/command-only, wired the same way <see cref="ICommandDispatcher" /> and the
///     rests of the chat core treat any <see cref="UserSession" />. PASS is checked against the
///     account password using the same MD5-then-bcrypt flow as client login: the osu! client sends
///     the MD5 of its password as hex at login, while an IRC client sends the password in plaintext,
///     so the plaintext PASS is MD5-hashed here before verification.
/// </remarks>
public sealed class IrcAuthenticationService(
	IUserRepository users,
	ISessionRegistry<IrcSession> ircSessions,
	IChannelRegistry channelRegistry,
	ChannelMembershipService channelMembership,
	IrcQueryService queries,
	IOptions<IrcOptions> options,
	IPasswordHasher passwordHasher,
	ITokenGenerator tokenGenerator)
{
	/// <summary>
	///     Validates an IRC nick and password against the stored account and, on success, creates the
	///     session and builds the welcome message sequence for a fresh IRC login.
	/// </summary>
	/// <param name="nick">The nick the connection wants, treated as the userSession's username.</param>
	/// <param name="pass">The plaintext password supplied via the PASS command.</param>
	/// <param name="connection">The IRC connection being authenticated.</param>
	/// <param name="cancellationToken">The cancellation token to observe.</param>
	/// <returns>
	///     An <see cref="IrcLoginOutcome" /> that either describes the failure with the numeric reply
	///     to send or carries the new session and the welcome, topic, and names messages to emit.
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

		var loginTime = DateTimeOffset.UtcNow;
		var session = new IrcSession(
			user.Id, user.Name, $"irc-{tokenGenerator.GenerateToken()}", user.Privilege, loginTime)
		{
			SilenceEnd = user.SilenceEnd,
			IrcConnection = connection
		};

		if (!ircSessions.TryAdd(session))
			return IrcLoginOutcome.Failed(
				IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.ErrNicknameInUse, nick,
					"Nickname is already in use"));

		var messages = new List<IrcMessage>
		{
			IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplWelcome, user.Name,
				$"Welcome to {options.Value.Name} IRC, {user.Name}")
		};
		messages.AddRange(queries.BuildWelcomeBurst(user.Name));

		foreach (var channel in channelRegistry.AutoJoinChannels)
		{
			if (!channel.CanRead(user.Privilege)) continue;

			channelMembership.Join(session, channel);

			if (!string.IsNullOrEmpty(channel.Topic))
				messages.Add(IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplTopic, user.Name,
					channel.Name,
					channel.Topic));

			messages.AddRange(channelMembership.BuildNamesReply(user.Name, channel));
		}

		return IrcLoginOutcome.Ok(session, messages);
	}
}
