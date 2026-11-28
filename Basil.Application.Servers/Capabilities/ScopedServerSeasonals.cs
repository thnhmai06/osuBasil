using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerSeasonals
{
	private readonly ScopedServer _scoped;

	internal ScopedServerSeasonals(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	private ServerActionResult? Denied()
	{
		return _scoped.Lacks(UserPrivileges.Owner)
			? ServerActionResult.Denied
			: null;
	}

	public async Task<(ServerActionResult Result, ContentReference? Image)> CreateAsync(string name,
		Stream image)
	{
		if (Denied() is { } denied) return (denied, null);

		return await _scoped.Server.Seasonals.CreateAsync(name, image);
	}

	public async Task<ServerActionResult> DeleteAsync(string image)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Seasonals.DeleteAsync(image);
	}
}