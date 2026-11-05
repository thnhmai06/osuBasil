using Basil.Application.Content.Persistence;
using Basil.Domain.Content;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Creates or replaces a FAQ entry.</summary>
	/// <param name="entry">The entry name; <c>:</c> separates folders.</param>
	/// <param name="text">The text of the entry.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok.</returns>
	/// <exception cref="ArgumentException">The entry name is not a safe name.</exception>
	internal async Task<ServerOutcome> SaveFaqAsync(string entry, string text, CancellationToken cancellationToken)
	{
		await using var writer = await Context.Faqs.CreateOrUpdateAsync(entry, cancellationToken);
		await writer.WriteAsync(text);
		writer.Commit();
		return ServerOutcome.Ok;
	}

	/// <summary>Deletes a FAQ entry.</summary>
	/// <param name="entry">The entry name.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok; deleting an entry that does not exist is ok.</returns>
	/// <exception cref="ArgumentException">The entry name is not a safe name.</exception>
	internal async Task<ServerOutcome> DeleteFaqAsync(string entry, CancellationToken cancellationToken)
	{
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
	/// <returns>Ok and the banner.</returns>
	/// <remarks>When storing the banner fails, the stored image is deleted again and the failure is rethrown.</remarks>
	/// <exception cref="ArgumentException">The name or the address is not valid.</exception>
	internal async Task<(ServerOutcome Outcome, MenuBanner Banner)> AddBannerAsync(string name, Stream image, Uri url,
		DateTimeOffset? startsAt, DateTimeOffset? endsAt, CancellationToken cancellationToken)
	{
		var banner = new MenuBanner
		{
			Image = await Context.BannerImages.SaveAsync(name, image, cancellationToken),
			Url = url,
			StartsAt = startsAt,
			EndsAt = endsAt,
			CreatedAt = Context.Connections.Clock.GetUtcNow()
		};
		try
		{
			await Context.Banners.CreateOrUpdateAsync(banner, cancellationToken);
		}
		catch
		{
			await Context.BannerImages.DeleteAsync(banner.Image, CancellationToken.None);
			throw;
		}

		return (ServerOutcome.Ok, banner);
	}

	/// <summary>Deletes a main-menu banner and its image.</summary>
	/// <param name="banner">The banner.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok.</returns>
	internal async Task<ServerOutcome> DeleteBannerAsync(MenuBanner banner, CancellationToken cancellationToken)
	{
		await Context.Banners.DeleteAsync(banner, cancellationToken);
		await Context.BannerImages.DeleteAsync(banner.Image, cancellationToken);
		return ServerOutcome.Ok;
	}

	/// <summary>Adds a seasonal background.</summary>
	/// <param name="name">The file name of the image.</param>
	/// <param name="image">The image.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok and the reference of the image.</returns>
	/// <exception cref="ArgumentException">The name is not valid.</exception>
	internal async Task<(ServerOutcome Outcome, ContentReference Image)> AddSeasonalAsync(string name, Stream image,
		CancellationToken cancellationToken)
	{
		return (ServerOutcome.Ok, await Context.Seasonals.SaveAsync(name, image, cancellationToken));
	}

	/// <summary>Deletes a seasonal background.</summary>
	/// <param name="image">The reference of the image.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok.</returns>
	internal async Task<ServerOutcome> DeleteSeasonalAsync(ContentReference image, CancellationToken cancellationToken)
	{
		await Context.Seasonals.DeleteAsync(image, cancellationToken);
		return ServerOutcome.Ok;
	}

	/// <summary>Sets the main-menu icon: its image, the address it links to, or both.</summary>
	/// <param name="name">The file name of the image, or <see langword="null" /> to keep the image.</param>
	/// <param name="image">The image, or <see langword="null" /> to keep the image.</param>
	/// <param name="url">The address the icon links to, or <see langword="null" /> for none.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>Ok.</returns>
	/// <exception cref="ArgumentException">The name or the address is not valid.</exception>
	internal async Task<ServerOutcome> SetIconAsync(string? name, Stream? image, Uri? url,
		CancellationToken cancellationToken)
	{
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
	/// <returns>Ok.</returns>
	internal async Task<ServerOutcome> ClearIconAsync(CancellationToken cancellationToken)
	{
		await Context.Icon.DeleteAsync(cancellationToken);
		var settings = await Context.Settings.GetAsync(cancellationToken);
		await Context.Settings.CreateOrUpdateAsync(settings with { MenuIcon = new ServerSettings.MenuIconSettings() },
			cancellationToken);
		return ServerOutcome.Ok;
	}
}
