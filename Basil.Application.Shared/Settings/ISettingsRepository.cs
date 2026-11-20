namespace Basil.Application.Shared.Settings;

public interface ISettingsRepository
{
	ServerSettings Current { get; }

	ValueTask<ServerSettings> GetAsync(CancellationToken ct = default);

	Task CreateOrUpdateAsync(ServerSettings settings);
}