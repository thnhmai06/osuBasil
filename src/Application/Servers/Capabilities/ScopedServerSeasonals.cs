using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerSeasonals
{
	private readonly ScopedServer _scoped;

	internal ScopedServerSeasonals(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	private ServerOutcome? Denied()
	{
		return _scoped.Lacks(UserPrivileges.Owner)
			? ServerOutcome.Denied
			: null;
	}

	public async Task<(ServerOutcome Result, ContentReference? Image)> CreateAsync(string name,
		Stream image)
	{
		if (Denied() is { } denied) return (denied, null);

		return await _scoped.Server.Seasonals.CreateAsync(name, image);
	}

	public async Task<ServerOutcome> DeleteAsync(ContentReference image)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Seasonals.DeleteAsync(image);
	}
}