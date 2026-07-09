using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Domain.Users;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Options;

namespace Basil.Application.UseCases.Irc;

/// <summary>
///     Authenticates a real IRC connection's PASS/NICK/USER handshake and, on success, wires up a
///     virtual <see cref="PlayerSession" /> the same way <c>BotBootstrapService</c> does for
///     BanchoBot — no bancho socket behind it, just a session <see cref="ICommandDispatcher" /> and
///     the rest of the chat core can treat identically to a real osu! client.
///     PASS is checked against the user's account password (same bcrypt/MD5 flow as osu! client
///     login — the client sends MD5(password) hex at login, IRC sends plaintext PASS which we
///     MD5 here before bcrypt verify).
/// </summary>
public sealed class IrcAuthenticationService(
    IUserRepository users,
    IPlayerSessionRegistry sessionRegistry,
    IChannelRegistry channelRegistry,
    ChannelMembershipService channelMembership,
    IOptions<IrcOptions> options,
    IClock clock,
    IPasswordHasher passwordHasher)
{
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

        var loginTime = clock.UtcNow.ToUnixTimeSeconds();
        var priv = (Privileges)user.Priv;
        var session = new PlayerSession(user.Id, user.Name, $"irc-{Guid.NewGuid():N}", priv, loginTime)
        {
            SilenceEnd = user.SilenceEnd,
            DonorEnd = user.DonorEnd,
            IrcConnection = connection
        };

        sessionRegistry.Add(session);

        var messages = new List<IrcMessage>
        {
            IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplWelcome, user.Name,
                $"Welcome to Basil IRC, {user.Name}")
        };

        foreach (var channel in channelRegistry.AutoJoinChannels)
        {
            if (!channel.CanRead(priv)) continue;

            channelMembership.Join(session, channel);

            if (!string.IsNullOrEmpty(channel.Topic))
                messages.Add(IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.RplTopic, user.Name,
                    channel.Name,
                    channel.Topic));

            messages.AddRange(BuildNamesReply(user.Name, channel));
        }

        return IrcLoginOutcome.Ok(session, messages);
    }

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

    /// <summary>Ported from help.ppy.sh's IRC page: `@` = chat moderator, `+` = connected via external IRC client.</summary>
    private static string NamePrefix(PlayerSession member)
    {
        if ((member.Priv & Privileges.Moderator) != 0) return "@";

        return member.IrcConnection.IsExternalIrcClient ? "+" : "";
    }
}