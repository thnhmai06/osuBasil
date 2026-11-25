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

	public async Task<(ServerActionResult Result, ServerSettings? Settings)> UpdateAsync(
		Func<ServerSettings, ServerSettings> change)
	{
		if (_scoped.Lacks(UserPrivileges.Developer))
			return (ServerActionResult.Denied, null);

		var scoped = _scoped;
		return await _scoped.Server.Settings.UpdateAsync(change,
			(current, changed) => changed.Accounts.DefaultPrivileges == current.Accounts.DefaultPrivileges ||
			                      !scoped.Lacks(changed.Accounts.DefaultPrivileges));
	}
}