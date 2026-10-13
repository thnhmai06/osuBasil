using Basil.Domain.Chat;

namespace Basil.Application.Storage.Contracts.Chat;

/// <summary>Stores the general chat channels the server offers.</summary>
public interface IChannelRepository
{
	/// <summary>Lists every general channel.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The general channels.</returns>
	Task<IEnumerable<GeneralChannel>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores a general channel, adding it or replacing the stored channel with the same name.</summary>
	/// <param name="channel">The channel to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(GeneralChannel channel, CancellationToken cancellationToken = default);

	/// <summary>Deletes a general channel; deleting a missing one does nothing.</summary>
	/// <param name="channel">The channel to delete.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task DeleteAsync(GeneralChannel channel, CancellationToken cancellationToken = default);
}