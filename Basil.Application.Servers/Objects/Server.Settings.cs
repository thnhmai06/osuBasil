using Basil.Application.Shared.Settings;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	internal readonly SemaphoreSlim SettingsGate = new(1, 1);

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

	internal async Task<(ServerActionResult Result, ServerSettings? Settings)> UpdateAsync(
		Func<ServerSettings, ServerSettings> change, Func<ServerSettings, ServerSettings, bool> permits)
	{
		await _server.SettingsGate.WaitAsync();
		try
		{
			var current = await _server.Context.Settings.GetAsync();
			var changed = change(current);
			if (!permits(current, changed)) return (ServerActionResult.Denied, null);

			await _server.Context.Settings.CreateOrUpdateAsync(changed);
			return (ServerActionResult.Ok, changed);
		}
		finally
		{
			_server.SettingsGate.Release();
		}
	}
}