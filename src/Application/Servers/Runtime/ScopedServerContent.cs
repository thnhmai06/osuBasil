using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Domain.Content;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Runtime;

/// <summary>Changes the FAQ entries and the main menu; reading them needs no permission.</summary>
/// <remarks>Every change requires <see cref="UserPermissions.ServerManageContent" />.</remarks>
public sealed class ScopedServerContent
{
	private readonly ScopedServer _scoped;

	internal ScopedServerContent(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	private ServerContext Context => _scoped.Server.Context;

	private Result<ServerOutcome>? Denied()
	{
		return _scoped.Needs(UserPermissions.ServerManageContent) is { } missing
			? Result<ServerOutcome>.Denied(missing)
			: null;
	}

	/// <summary>Creates or replaces a FAQ entry.</summary>
	/// <param name="entry">The entry name; <c>:</c> separates folders.</param>
	/// <param name="text">The text of the entry.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome.</returns>
	/// <exception cref="ArgumentException">The entry name is not a safe name.</exception>
	public async Task<Result<ServerOutcome>> SaveFaqAsync(string entry, string text,
		CancellationToken cancellationToken = default)
	{
		if (Denied() is { } denied) return denied;

		await using var writer = await Context.Faqs.CreateOrUpdateAsync(entry, cancellationToken);
		await writer.WriteAsync(text);
		writer.Commit();
		return ServerOutcome.Ok;
	}

	/// <summary>Deletes a FAQ entry.</summary>
	/// <param name="entry">The entry name.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome; deleting an entry that does not exist is ok.</returns>
	/// <exception cref="ArgumentException">The entry name is not a safe name.</exception>
	public async Task<Result<ServerOutcome>> DeleteFaqAsync(string entry, CancellationToken cancellationToken = default)
	{
		if (Denied() is { } denied) return denied;

		await Context.Faqs.DeleteAsync(entry, cancellationToken);
		return ServerOutcome.Ok;
	}

	/// <summary>Adds a main-menu banner whose image is uploaded.</summary>
	/// <param name="name">The file name of the image.</param>
	/// <param name="image">The image.</param>
	/// <param name="url">The address the banner links to.</param>
	/// <param name="startsAt">When it starts showing, or <see langword="null" /> for now.</param>
	/// <param name="endsAt">When it stops showing, or <see langword="null" /> for never.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the banner.</returns>
	/// <exception cref="ArgumentException">The name or the address is not valid.</exception>
	public async Task<(Result<ServerOutcome> Result, MenuBanner? Banner)> AddBannerAsync(string name, Stream image,
		Uri url, DateTimeOffset? startsAt, DateTimeOffset? endsAt, CancellationToken cancellationToken = default)
	{
		if (Denied() is { } denied) return (denied, null);

		var banner = new MenuBanner
		{
			Image = await Context.BannerImages.SaveAsync(name, image, cancellationToken),
			Url = url,
			StartsAt = startsAt,
			EndsAt = endsAt,
			CreatedAt = _scoped.Server.Context.Connections.Clock.GetUtcNow()
		};
		await Context.Banners.CreateOrUpdateAsync(banner, cancellationToken);
		return (ServerOutcome.Ok, banner);
	}

	/// <summary>Deletes a main-menu banner and its image.</summary>
	/// <param name="banner">The banner.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome.</returns>
	public async Task<Result<ServerOutcome>> DeleteBannerAsync(MenuBanner banner,
		CancellationToken cancellationToken = default)
	{
		if (Denied() is { } denied) return denied;

		await Context.Banners.DeleteAsync(banner, cancellationToken);
		await Context.BannerImages.DeleteAsync(banner.Image, cancellationToken);
		return ServerOutcome.Ok;
	}

	/// <summary>Adds a seasonal background.</summary>
	/// <param name="name">The file name of the image.</param>
	/// <param name="image">The image.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when successful, the reference of the image.</returns>
	/// <exception cref="ArgumentException">The name is not valid.</exception>
	public async Task<(Result<ServerOutcome> Result, ContentReference? Image)> AddSeasonalAsync(string name,
		Stream image, CancellationToken cancellationToken = default)
	{
		if (Denied() is { } denied) return (denied, null);

		return (ServerOutcome.Ok, await Context.Seasonals.SaveAsync(name, image, cancellationToken));
	}

	/// <summary>Deletes a seasonal background.</summary>
	/// <param name="image">The reference of the image.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome.</returns>
	public async Task<Result<ServerOutcome>> DeleteSeasonalAsync(ContentReference image,
		CancellationToken cancellationToken = default)
	{
		if (Denied() is { } denied) return denied;

		await Context.Seasonals.DeleteAsync(image, cancellationToken);
		return ServerOutcome.Ok;
	}

	/// <summary>Sets the main-menu icon: its image, the address it links to, or both.</summary>
	/// <param name="name">The file name of the image, or <see langword="null" /> to keep the image.</param>
	/// <param name="image">The image, or <see langword="null" /> to keep the image.</param>
	/// <param name="url">The address the icon links to, or <see langword="null" /> for none.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome.</returns>
	/// <exception cref="ArgumentException">The name or the address is not valid.</exception>
	public async Task<Result<ServerOutcome>> SetIconAsync(string? name, Stream? image, Uri? url,
		CancellationToken cancellationToken = default)
	{
		if (Denied() is { } denied) return denied;

		var settings = await Context.Settings.GetAsync(cancellationToken);
		var reference = name is not null && image is not null
			? await Context.Icon.SaveAsync(name, image, cancellationToken)
			: settings.MenuIcon.Image;
		await Context.Settings.CreateOrUpdateAsync(
			settings with { MenuIcon = new ServerSettings.MenuIconSettings { Url = url, Image = reference } },
			cancellationToken);
		return ServerOutcome.Ok;
	}

	/// <summary>Removes the main-menu icon.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome.</returns>
	public async Task<Result<ServerOutcome>> ClearIconAsync(CancellationToken cancellationToken = default)
	{
		if (Denied() is { } denied) return denied;

		await Context.Icon.DeleteAsync(cancellationToken);
		var settings = await Context.Settings.GetAsync(cancellationToken);
		await Context.Settings.CreateOrUpdateAsync(settings with { MenuIcon = new ServerSettings.MenuIconSettings() },
			cancellationToken);
		return ServerOutcome.Ok;
	}
}
