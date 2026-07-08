namespace Basil.Application.Abstractions.Social;

/// <summary>Ported from app/repositories/mail.py's Mail dataclass.</summary>
public sealed record Mail(int Id, int FromId, int ToId, string Msg, int? Time, bool Read);

/// <summary>Ported from app/repositories/mail.py's MailWithUsernames dataclass.</summary>
public sealed record MailWithUsernames(
    int Id,
    int FromId,
    int ToId,
    string Msg,
    int? Time,
    bool Read,
    string FromName,
    string ToName);

/// <summary>
///     Ported from app/repositories/mail.py's MailRepository, scoped to what login needs: unread
///     mail delivery and marking a conversation read (the client sends this after displaying mail).
/// </summary>
public interface IMailRepository
{
    Task<Mail> CreateAsync(int fromId, int toId, string msg, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailWithUsernames>> FetchUnreadMailToUserAsync(int userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Mail>> MarkConversationAsReadAsync(int toId, int fromId,
        CancellationToken cancellationToken = default);
}