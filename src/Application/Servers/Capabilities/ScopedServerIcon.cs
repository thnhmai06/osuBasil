using Basil.Application.Servers.Objects;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerIcon
{
	private readonly ScopedServer _scoped;

	internal ScopedServerIcon(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	private ServerOutcome? Denied()
	{
		return _scoped.Lacks(UserPrivileges.Owner)
			? ServerOutcome.Denied
			: null;
	}

	public async Task<ServerOutcome> ReplaceAsync(string? name, Stream? image, Uri? url)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Icon.ReplaceAsync(name, image, url);
	}

	public async Task<ServerOutcome> DeleteAsync()
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Icon.DeleteAsync();
	}
}