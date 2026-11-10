using Basil.Application.Content.Persistence;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Reads the settings in force.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The settings.</returns>
	internal async Task<ServerSettings> GetSettingsAsync(CancellationToken ct = default)
	{
		return await Context.Settings.GetAsync(ct);
	}

	/// <summary>Replaces the settings; the change applies at once and survives a restart.</summary>
	/// <param name="settings">The new settings.</param>
	/// <returns>Ok.</returns>
	internal async Task<ServerOutcome> UpdateSettingsAsync(ServerSettings settings)
	{
		await Context.Settings.CreateOrUpdateAsync(settings);
		return ServerOutcome.Ok;
	}
}