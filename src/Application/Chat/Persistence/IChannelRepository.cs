using Basil.Application.Shared.Paging;
using Basil.Domain.Chat;

namespace Basil.Application.Chat.Persistence;

/// <summary>Stores the general chat channels the server offers.</summary>
public interface IChannelRepository
{
	/// <summary>Lists every general channel, by name.</summary>
	/// <remarks>At most <see cref="ListLimits.Ceiling" /> channels are returned.</remarks>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The general channels.</returns>
	Task<IEnumerable<GeneralChannelRecord>> ListAsync(CancellationToken ct = default);

	/// <summary>Stores a general channel, adding it or replacing the stored channel with the same name.</summary>
	/// <param name="channel">The channel to store.</param>
	Task CreateOrUpdateAsync(GeneralChannelRecord channel);

	/// <summary>Deletes a general channel; deleting a missing one does nothing.</summary>
	/// <param name="channel">The channel to delete.</param>
	Task DeleteAsync(GeneralChannelRecord channel);
}