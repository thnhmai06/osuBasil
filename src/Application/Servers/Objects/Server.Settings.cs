using Basil.Application.Content.Persistence;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Reads the settings in force.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The settings.</returns>
	internal async Task<ServerSettings> GetSettingsAsync(CancellationToken cancellationToken)
	{
		return await Context.Settings.GetAsync(cancellationToken);
	}

	/// <summary>Replaces the settings; the change applies at once and survives a restart.</summary>
	/// <param name="settings">The new settings.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok.</returns>
	internal async Task<ServerOutcome> UpdateSettingsAsync(ServerSettings settings,
		CancellationToken cancellationToken)
	{
		await Context.Settings.CreateOrUpdateAsync(settings);
		return ServerOutcome.Ok;
	}
}