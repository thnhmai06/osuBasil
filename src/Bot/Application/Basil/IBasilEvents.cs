using Basil.Domain.Beatmaps;
using Basil.Domain.Users;

namespace Basil.Bot.Application.Basil;

/// <summary>The stream of things happening on the server that the bot reacts to.</summary>
public interface IBasilEvents
{
	/// <summary>Reads events as they happen until cancelled.</summary>
	/// <param name="cancellationToken">A token that stops reading.</param>
	/// <returns>The events, in the order they happened.</returns>
	IAsyncEnumerable<BotEvent> ReadAllAsync(CancellationToken cancellationToken = default);
}

