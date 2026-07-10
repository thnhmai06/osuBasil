using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.UseCases.Bot;
using Basil.Protocol.Irc;
using Basil.Protocol.Packets;
using Action = Basil.Domain.Action;

namespace Basil.Application.UseCases.Chat;

/// <summary>
///     Single entry point for "a sender said `text` to `channelOrNick`" — used identically by bancho's
///     SendPublicMessage/SendPrivateMessage handlers and by a real IRC connection's PRIVMSG. A leading
///     '#' routes to the channel path (broadcast + `!`-command dispatch); anything else resolves to a
///     user (bot command shortcut, or block/away/silence-checked delivery + offline mail).
///     Lives one layer above <see cref="ChannelMembershipService" /> specifically to avoid a DI cycle:
///     this depends on <see cref="ICommandDispatcher" />, which chains back down to
///     <c>MatchMembershipService</c> -&gt; <see cref="ChannelMembershipService" /> — that class itself
///     must stay free of any dependency on this one.
/// </summary>
public sealed class ChatDispatchService(
    IChannelRegistry channelRegistry,
    IPlayerSessionRegistry sessionRegistry,
    ChannelMembershipService channelMembership,
    IUserRepository users,
    IRelationshipRepository relationships,
    IMailRepository mail,
    ICommandDispatcher commandDispatcher)
{
    private const int MaxMessageLength = 2000;

    public async Task SendPrivmsgAsync(PlayerSession sender, string channelOrNick, string text,
        CancellationToken cancellationToken = default)
    {
        if (sender.Silenced) return;

        if (channelOrNick.StartsWith('#'))
        {
            await SendChannelMessageAsync(sender, channelOrNick, text, cancellationToken);
            return;
        }

        var target = sessionRegistry.GetByName(channelOrNick);
        if (target is { IsBot: true })
        {
            await SendBotCommandAsync(sender, target, text, cancellationToken);
            return;
        }

        await DeliverPrivateMessageAsync(sender, channelOrNick, target, text, cancellationToken);
    }

    private async Task SendChannelMessageAsync(PlayerSession sender, string channelName, string text,
        CancellationToken cancellationToken)
    {
        var channel = channelRegistry.GetByName(channelName);
        if (channel is null || !channel.Contains(sender.Id) || !channel.CanWrite(sender.Priv)) return;

        var truncated = text.Length > MaxMessageLength ? text[..MaxMessageLength] : text;

        channelMembership.BroadcastPrivmsg(
            channel, IrcMessageWriter.Privmsg(sender.Name, sender.Id, channel.Name, truncated),
            skipMemberId: sender.Id);

        var matchScope = sender.Match is not null && sender.Match.ChatChannelName == channel.Name
            ? sender.Match
            : null;
        var reply = await commandDispatcher.DispatchAsync(sender, truncated, matchScope, cancellationToken: cancellationToken);
        if (reply is null) return;

        var bot = sessionRegistry.GetById(BotBootstrapService.BotId);
        if (bot is null) return;

        // A reply may embed `\n` (e.g. !faq's file contents) — each line becomes its own chat message,
        // matching how a real client displays multiple consecutive lines rather than one with a
        // visible newline. Every other command's reply has no `\n`, so this is a no-op for those.
        foreach (var line in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            channelMembership.BroadcastPrivmsg(channel, IrcMessageWriter.Privmsg(bot.Name, bot.Id, channel.Name, line));
    }

    private async Task SendBotCommandAsync(PlayerSession sender, PlayerSession bot, string text,
        CancellationToken cancellationToken)
    {
        var reply = await commandDispatcher.DispatchAsync(sender, text, null, prefixOptional: true,
            cancellationToken: cancellationToken);
        if (reply is null) return;

        // See SendChannelMessageAsync's matching split — a `\n`-embedded reply (e.g. !faq) becomes one
        // chat message per line instead of one message with a visible newline.
        foreach (var line in reply.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            sender.IrcConnection.Send(IrcMessageWriter.Privmsg(bot.Name, bot.Id, bot.Name, line));
    }

    private async Task DeliverPrivateMessageAsync(PlayerSession sender, string recipientName, PlayerSession? target,
        string text, CancellationToken cancellationToken)
    {
        int targetId;
        if (target is not null)
        {
            targetId = target.Id;
        }
        else
        {
            var targetUser = await users.FetchByNameAsync(recipientName, cancellationToken);
            if (targetUser is null) return;

            targetId = targetUser.Id;
        }

        var relationship = await relationships.FetchOneAsync(targetId, sender.Id, cancellationToken);
        if (relationship?.Type == RelationshipType.Block)
        {
            sender.Enqueue(ServerPacketWriter.UserDmBlocked(recipientName));
            return;
        }

        if (target is not null)
        {
            if (target.PmPrivate && relationship?.Type != RelationshipType.Friend)
            {
                sender.Enqueue(ServerPacketWriter.UserDmBlocked(recipientName));
                return;
            }

            if (target.Silenced)
            {
                sender.Enqueue(ServerPacketWriter.TargetSilenced(recipientName));
                return;
            }

            target.IrcConnection.Send(IrcMessageWriter.Privmsg(sender.Name, sender.Id, recipientName, text));

            if (target.Status.Action == Action.Afk && target.AwayMessage is { } awayMessage)
                sender.IrcConnection.Send(IrcMessageWriter.Privmsg(target.Name, target.Id, sender.Name, awayMessage));
        }

        await mail.CreateAsync(sender.Id, targetId, text, cancellationToken);
    }
}
