using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerSettings
{
	private readonly ScopedServer _scoped;

	internal ScopedServerSettings(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	public async Task<(ServerActionResult Result, ServerSettings? Settings)> GetAsync(
		CancellationToken ct = default)
	{
		return _scoped.Lacks(UserPrivileges.Developer)
			? (ServerActionResult.Denied, null)
			: (ServerActionResult.Ok, await _scoped.Server.Settings.GetAsync(ct));
	}

	public async Task<ServerActionResult> UpdateAsync(ServerSettings settings)
	{
		if (_scoped.Lacks(UserPrivileges.Developer))
			return ServerActionResult.Denied;

		var current = await _scoped.Server.Settings.GetAsync();
		if (settings.Accounts.DefaultPrivileges != current.Accounts.DefaultPrivileges &&
		    _scoped.Lacks(settings.Accounts.DefaultPrivileges))
			return ServerActionResult.Denied;

		return await _scoped.Server.Settings.UpdateAsync(settings);
	}
}