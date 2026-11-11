using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>Changes the seasonal backgrounds; every change requires <see cref="UserPrivileges.Owner" />.</summary>
public readonly struct ScopedServerSeasonals
{
	private readonly ScopedServer _scoped;

	/// <summary>Creates the scoped view of the seasonal backgrounds.</summary>
	/// <param name="scoped">The scoped server.</param>
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

	/// <summary>Adds a seasonal background.</summary>
	/// <param name="name">The file name of the image.</param>
	/// <param name="image">The image.</param>
	/// <returns>The outcome and, when successful, the reference of the image.</returns>
	/// <exception cref="ArgumentException">The name is not valid.</exception>
	public async Task<(ServerOutcome Result, ContentReference? Image)> CreateAsync(string name,
		Stream image)
	{
		if (Denied() is { } denied) return (denied, null);

		return await _scoped.Server.Seasonals.CreateAsync(name, image);
	}

	/// <summary>Deletes a seasonal background.</summary>
	/// <param name="image">The reference of the image.</param>
	/// <returns>The outcome.</returns>
	public async Task<ServerOutcome> DeleteAsync(ContentReference image)
	{
		if (Denied() is { } denied) return denied;

		return await _scoped.Server.Seasonals.DeleteAsync(image);
	}
}