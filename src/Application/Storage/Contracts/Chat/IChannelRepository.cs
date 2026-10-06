using Basil.Domain.Chat;

namespace Basil.Application.Storage.Contracts.Chat;

/// <summary>Stores the general chat channels the server offers.</summary>
public interface IChannelRepository
{
	/// <summary>Lists every general channel.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The general channels.</returns>
	Task<IReadOnlyList<GeneralChannel>> ListAsync(CancellationToken cancellationToken = default);
}