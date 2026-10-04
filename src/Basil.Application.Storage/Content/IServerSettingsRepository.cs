using Basil.Domain.Content;

namespace Basil.Application.Storage.Content;

/// <summary>Stores the server-wide settings.</summary>
public interface IServerSettingsRepository
{
	/// <summary>Gets the server-wide settings.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The stored settings, or settings with every value unset when none are stored. Never <see langword="null" />.</returns>
	ValueTask<ServerSettings> GetAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores the server-wide settings, replacing the stored ones.</summary>
	/// <param name="settings">The settings to store.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	Task CreateOrUpdateAsync(ServerSettings settings, CancellationToken cancellationToken = default);
}