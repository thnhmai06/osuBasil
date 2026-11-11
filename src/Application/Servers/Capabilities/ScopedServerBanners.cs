using Basil.Application.Shared;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>Changes the main-menu banners; every change requires <see cref="UserPrivileges.Owner" />.</summary>
public readonly struct ScopedServerBanners
{
	private readonly ScopedServer _scoped;

	/// <summary>Creates the scoped view of the main-menu banners.</summary>
	/// <param name="scoped">The scoped server.</param>
	internal ScopedServerBanners(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	private ServerOutcome? Denied()
	{
		return _scoped.Lacks(UserPrivileges.Owner)
			? ServerOutcome.Denied
			: null;
	}

	/// <summary>Adds a main-menu banner whose image is uploaded.</summary>
	/// <param name="name">The file name of the image.</param>
	/// <param name="image">The image.</param>
	/// <param name="url">The address the banner links to.</param>
	/// <param name="startsAt">When it starts showing, or <see langword="null" /> for now.</param>
	/// <param name="endsAt">When it stops showing, or <see langword="null" /> for never.</param>
	/// <returns>The outcome and, when successful, the banner.</returns>
	/// <exception cref="ArgumentException">The name or the address is not valid.</exception>
	public async Task<(ServerOutcome Result, MenuBanner? Banner)> CreateAsync(string name, Stream image,
		Uri url, DateTimeOffset? startsAt, DateTimeOffset? endsAt)
	{
		if (Denied() is { } denied) return (denied, null);

		return await _scoped.Server.Banners.CreateAsync(name, image, url, startsAt, endsAt);
	}

	/// <summary>Deletes a main-menu banner and its image.</summary>
	/// <param name="banner">The banner.</param>
	/// <returns>The outcome.</returns>
	public async Task<ServerOutcome> DeleteAsync(MenuBanner banner)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Banners.DeleteAsync(banner);
	}
}