using Basil.Application.Common.Settings;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Gets the runtime settings of the server, read and changed without a privilege check.</summary>
	public ServerSettingsView Settings => new(this);
}

/// <summary>The runtime settings of the server, read and changed without a privilege check.</summary>
public readonly struct ServerSettingsView
{
	private readonly Server _server;

	/// <summary>Creates the view of a server.</summary>
	/// <param name="server">The server.</param>
	internal ServerSettingsView(Server server)
	{
		_server = server;
	}

	/// <summary>Reads the settings in force.</summary>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The settings.</returns>
	internal async Task<ServerSettings> GetAsync(CancellationToken ct = default)
	{
		return await _server.Context.Settings.GetAsync(ct);
	}

	/// <summary>Replaces the settings; the change applies at once and survives a restart.</summary>
	/// <param name="settings">The new settings.</param>
	/// <returns>Ok.</returns>
	internal async Task<ServerOutcome> UpdateAsync(ServerSettings settings)
	{
		await _server.Context.Settings.CreateOrUpdateAsync(settings);
		return ServerOutcome.Ok;
	}
}