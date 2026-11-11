using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>Changes the main-menu icon; every change requires <see cref="UserPrivileges.Owner" />.</summary>
public readonly struct ScopedServerIcon
{
	private readonly ScopedServer _scoped;

	/// <summary>Creates the scoped view of the main-menu icon.</summary>
	/// <param name="scoped">The scoped server.</param>
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

	/// <summary>Sets the main-menu icon: its image, the address it links to, or both.</summary>
	/// <param name="name">The file name of the image, or <see langword="null" /> to keep the image.</param>
	/// <param name="image">The image, or <see langword="null" /> to keep the image.</param>
	/// <param name="url">The address the icon links to, or <see langword="null" /> for none.</param>
	/// <returns>The outcome.</returns>
	/// <exception cref="ArgumentException">The name or the address is not valid.</exception>
	public async Task<ServerOutcome> ReplaceAsync(string? name, Stream? image, Uri? url)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Icon.ReplaceAsync(name, image, url);
	}

	/// <summary>Removes the main-menu icon.</summary>
	/// <returns>The outcome.</returns>
	public async Task<ServerOutcome> DeleteAsync()
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Icon.DeleteAsync();
	}
}