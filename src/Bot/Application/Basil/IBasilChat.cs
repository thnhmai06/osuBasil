using Basil.Domain.Users;

namespace Basil.Bot.Application.Basil;

/// <summary>Posts the bot's messages.</summary>
public interface IBasilChat
{
	/// <summary>Posts a message in a channel as the bot.</summary>
	/// <param name="channel">The channel name, such as <c>#mp_5</c>.</param>
	/// <param name="text">The message.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task PostAsync(string channel, string text, CancellationToken cancellationToken = default);

	/// <summary>Sends a private message to a user as the bot.</summary>
	/// <param name="user">The recipient.</param>
	/// <param name="text">The message.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task SendAsync(User user, string text, CancellationToken cancellationToken = default);
}

