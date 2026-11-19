using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerBanners
{
	private readonly ScopedServer _scoped;

	internal ScopedServerBanners(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	private ServerActionResult? Denied()
	{
		return _scoped.Lacks(UserPrivileges.Owner)
			? ServerActionResult.Denied
			: null;
	}

	public async Task<(ServerActionResult Result, MenuBanner? Banner)> CreateAsync(string name, Stream image,
		Uri url, DateTimeOffset? startsAt, DateTimeOffset? endsAt)
	{
		if (Denied() is { } denied) return (denied, null);

		return await _scoped.Server.Banners.CreateAsync(name, image, url, startsAt, endsAt);
	}

	public async Task<ServerActionResult> DeleteAsync(MenuBanner banner)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Banners.DeleteAsync(banner);
	}
}