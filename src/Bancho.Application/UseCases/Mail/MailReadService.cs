using Bancho.Application.Abstractions;
using Bancho.Application.Sessions;
using Bancho.Application.Abstractions.Social;
using Bancho.Application.Abstractions.Users;

namespace Bancho.Application.UseCases.Mail;

/// <summary>
/// Ported from app/services/mail.py's MailReadService.mark_channel_as_read, backing
/// osu-markasread.php. "channel" is the mail sender's player name — a naming quirk carried over
/// from the Python source, not an actual chat channel.
/// </summary>
public sealed class MailReadService(IPlayerSessionRegistry sessionRegistry, IUserRepository users, IMailRepository mail)
{
    public async Task MarkChannelAsReadAsync(PlayerSession player, string channel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(channel))
        {
            return;
        }

        var senderId = sessionRegistry.GetByName(channel)?.Id ?? (await users.FetchByNameAsync(channel, cancellationToken))?.Id;
        if (senderId is null)
        {
            return;
        }

        await mail.MarkConversationAsReadAsync(player.Id, senderId.Value, cancellationToken);
    }
}
