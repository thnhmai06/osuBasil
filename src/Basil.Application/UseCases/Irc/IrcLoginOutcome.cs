using Basil.Application.Sessions;
using Basil.Protocol.Irc;

namespace Basil.Application.UseCases.Irc;

/// <summary>Result of an IRC PASS/NICK/USER handshake attempt.</summary>
public sealed record IrcLoginOutcome(bool Success, PlayerSession? Session, IReadOnlyList<IrcMessage> Messages)
{
    public static IrcLoginOutcome Failed(IrcMessage error)
    {
        return new IrcLoginOutcome(false, null, [error]);
    }

    public static IrcLoginOutcome Ok(PlayerSession session, IReadOnlyList<IrcMessage> messages)
    {
        return new IrcLoginOutcome(true, session, messages);
    }
}
