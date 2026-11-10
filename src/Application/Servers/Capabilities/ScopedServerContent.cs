using Basil.Application.Common;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>Changes the FAQ entries and the main menu; reading them needs no privilege.</summary>
/// <remarks>Every change requires <see cref="UserPrivileges.Owner" />.</remarks>
public readonly struct ScopedServerContent
{
	private readonly ScopedServer _scoped;

	internal ScopedServerContent(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	private Server Server => _scoped.Server;

	private Result<ServerOutcome>? Denied()
	{
		return _scoped.Needs(UserPrivileges.Owner) is { } missing
			? Result<ServerOutcome>.Denied(missing)
			: null;
	}

	/// <summary>Creates or replaces a FAQ entry.</summary>
	/// <param name="entry">The entry name; <c>:</c> separates folders.</param>
	/// <param name="text">The text of the entry.</param>
	/// <returns>The outcome.</returns>
	/// <exception cref="ArgumentException">The entry name is not a safe name.</exception>
	public async Task<Result<ServerOutcome>> SaveFaqAsync(string entry, string text)
	{
		if (Denied() is { } denied) return denied;

		return await Server.SaveFaqAsync(entry, text);
	}

	/// <summary>Deletes a FAQ entry.</summary>
	/// <param name="entry">The entry name.</param>
	/// <returns>The outcome; deleting an entry that does not exist is ok.</returns>
	/// <exception cref="ArgumentException">The entry name is not a safe name.</exception>
	public async Task<Result<ServerOutcome>> DeleteFaqAsync(string entry)
	{
		if (Denied() is { } denied) return denied;

		return await Server.DeleteFaqAsync(entry);
	}

	/// <summary>Adds a main-menu banner whose image is uploaded.</summary>
	/// <param name="name">The file name of the image.</param>
	/// <param name="image">The image.</param>
	/// <param name="url">The address the banner links to.</param>
	/// <param name="startsAt">When it starts showing, or <see langword="null" /> for now.</param>
	/// <param name="endsAt">When it stops showing, or <see langword="null" /> for never.</param>
	/// <returns>The outcome and, when successful, the banner.</returns>
	/// <exception cref="ArgumentException">The name or the address is not valid.</exception>
	public async Task<(Result<ServerOutcome> Result, MenuBanner? Banner)> AddBannerAsync(string name, Stream image,
		Uri url, DateTimeOffset? startsAt, DateTimeOffset? endsAt)
	{
		if (Denied() is { } denied) return (denied, null);

		return await Server.AddBannerAsync(name, image, url, startsAt, endsAt);
	}

	/// <summary>Deletes a main-menu banner and its image.</summary>
	/// <param name="banner">The banner.</param>
	/// <returns>The outcome.</returns>
	public async Task<Result<ServerOutcome>> DeleteBannerAsync(MenuBanner banner)
	{
		if (Denied() is { } denied) return denied;

		return await Server.DeleteBannerAsync(banner);
	}

	/// <summary>Adds a seasonal background.</summary>
	/// <param name="name">The file name of the image.</param>
	/// <param name="image">The image.</param>
	/// <returns>The outcome and, when successful, the reference of the image.</returns>
	/// <exception cref="ArgumentException">The name is not valid.</exception>
	public async Task<(Result<ServerOutcome> Result, ContentReference? Image)> AddSeasonalAsync(string name,
		Stream image)
	{
		if (Denied() is { } denied) return (denied, null);

		return await Server.AddSeasonalAsync(name, image);
	}

	/// <summary>Deletes a seasonal background.</summary>
	/// <param name="image">The reference of the image.</param>
	/// <returns>The outcome.</returns>
	public async Task<Result<ServerOutcome>> DeleteSeasonalAsync(ContentReference image)
	{
		if (Denied() is { } denied) return denied;

		return await Server.DeleteSeasonalAsync(image);
	}

	/// <summary>Sets the main-menu icon: its image, the address it links to, or both.</summary>
	/// <param name="name">The file name of the image, or <see langword="null" /> to keep the image.</param>
	/// <param name="image">The image, or <see langword="null" /> to keep the image.</param>
	/// <param name="url">The address the icon links to, or <see langword="null" /> for none.</param>
	/// <returns>The outcome.</returns>
	/// <exception cref="ArgumentException">The name or the address is not valid.</exception>
	public async Task<Result<ServerOutcome>> SetIconAsync(string? name, Stream? image, Uri? url)
	{
		if (Denied() is { } denied) return denied;

		return await Server.SetIconAsync(name, image, url);
	}

	/// <summary>Removes the main-menu icon.</summary>
	/// <returns>The outcome.</returns>
	public async Task<Result<ServerOutcome>> ClearIconAsync()
	{
		if (Denied() is { } denied) return denied;

		return await Server.ClearIconAsync();
	}
}