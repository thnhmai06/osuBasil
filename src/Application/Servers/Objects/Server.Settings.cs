using Basil.Application.Shared.Settings;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	public ServerSettingsView Settings => new(this);
}

public readonly struct ServerSettingsView
{
	private readonly Server _server;

	internal ServerSettingsView(Server server)
	{
		_server = server;
	}

	internal async Task<ServerSettings> GetAsync(CancellationToken ct = default)
	{
		return await _server.Context.Settings.GetAsync(ct);
	}

	internal async Task<ServerOutcome> UpdateAsync(ServerSettings settings)
	{
		await _server.Context.Settings.CreateOrUpdateAsync(settings);
		return ServerOutcome.Ok;
	}
}