using Basil.Application.Shared.Settings;
using Basil.Domain.Content;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	/// <summary>Gets the FAQ entries of the server, changed without a privilege check.</summary>
	public ServerFaq Faq => new(this);

	/// <summary>Gets the main-menu banners of the server, changed without a privilege check.</summary>
	public ServerBanners Banners => new(this);

	/// <summary>Gets the seasonal backgrounds of the server, changed without a privilege check.</summary>
	public ServerSeasonals Seasonals => new(this);

	/// <summary>Gets the main-menu icon of the server, changed without a privilege check.</summary>
	public ServerIcon Icon => new(this);
}

/// <summary>The FAQ entries of the server, changed without a privilege check.</summary>
public readonly struct ServerFaq
{
	private readonly Server _server;

	/// <summary>Creates the view of a server.</summary>
	/// <param name="server">The server.</param>
	internal ServerFaq(Server server)
	{
		_server = server;
	}

	/// <summary>Creates or replaces a FAQ entry.</summary>
	/// <param name="entry">The entry name; <c>:</c> separates folders.</param>
	/// <param name="text">The text of the entry.</param>
	/// <returns>Ok.</returns>
	/// <exception cref="ArgumentException">The entry name is not a safe name.</exception>
	internal async Task<ServerOutcome> SaveAsync(string entry, string text)
	{
		await using var writer = await _server.Context.Faqs.CreateOrUpdateAsync(entry);
		await writer.WriteAsync(text);
		writer.Commit();
		return ServerOutcome.Ok;
	}

	/// <summary>Deletes a FAQ entry.</summary>
	/// <param name="entry">The entry name.</param>
	/// <returns>Ok; deleting an entry that does not exist is ok.</returns>
	/// <exception cref="ArgumentException">The entry name is not a safe name.</exception>
	internal async Task<ServerOutcome> DeleteAsync(string entry)
	{
		await _server.Context.Faqs.DeleteAsync(entry);
		return ServerOutcome.Ok;
	}
}

/// <summary>The main-menu banners of the server, changed without a privilege check.</summary>
public readonly struct ServerBanners
{
	private readonly Server _server;

	/// <summary>Creates the view of a server.</summary>
	/// <param name="server">The server.</param>
	internal ServerBanners(Server server)
	{
		_server = server;
	}

	/// <summary>Adds a main-menu banner whose image is uploaded.</summary>
	/// <param name="name">The file name of the image.</param>
	/// <param name="image">The image.</param>
	/// <param name="url">The address the banner links to.</param>
	/// <param name="startsAt">When it starts showing, or <see langword="null" /> for now.</param>
	/// <param name="endsAt">When it stops showing, or <see langword="null" /> for never.</param>
	/// <returns>Ok and the banner.</returns>
	/// <remarks>When storing the banner fails, the stored image is deleted again and the failure is rethrown.</remarks>
	/// <exception cref="ArgumentException">The name or the address is not valid.</exception>
	internal async Task<(ServerOutcome Outcome, MenuBanner Banner)> CreateAsync(string name, Stream image, Uri url,
		DateTimeOffset? startsAt, DateTimeOffset? endsAt)
	{
		var banner = new MenuBanner
		{
			Image = await _server.Context.BannerImages.SaveAsync(name, image),
			Url = url,
			StartsAt = startsAt,
			EndsAt = endsAt,
			CreatedAt = _server.Context.Connections.Clock.GetUtcNow()
		};
		try
		{
			await _server.Context.Banners.CreateOrUpdateAsync(banner);
		}
		catch
		{
			await _server.Context.BannerImages.DeleteAsync(banner.Image);
			throw;
		}

		return (ServerOutcome.Ok, banner);
	}

	/// <summary>Deletes a main-menu banner and its image.</summary>
	/// <param name="banner">The banner.</param>
	/// <returns>Ok.</returns>
	internal async Task<ServerOutcome> DeleteAsync(MenuBanner banner)
	{
		await _server.Context.Banners.DeleteAsync(banner);
		await _server.Context.BannerImages.DeleteAsync(banner.Image);
		return ServerOutcome.Ok;
	}
}

/// <summary>The seasonal backgrounds of the server, changed without a privilege check.</summary>
public readonly struct ServerSeasonals
{
	private readonly Server _server;

	/// <summary>Creates the view of a server.</summary>
	/// <param name="server">The server.</param>
	internal ServerSeasonals(Server server)
	{
		_server = server;
	}

	/// <summary>Adds a seasonal background.</summary>
	/// <param name="name">The file name of the image.</param>
	/// <param name="image">The image.</param>
	/// <returns>Ok and the reference of the image.</returns>
	/// <exception cref="ArgumentException">The name is not valid.</exception>
	internal async Task<(ServerOutcome Outcome, ContentReference Image)> CreateAsync(string name, Stream image)
	{
		return (ServerOutcome.Ok, await _server.Context.Seasonals.SaveAsync(name, image));
	}

	/// <summary>Deletes a seasonal background.</summary>
	/// <param name="image">The reference of the image.</param>
	/// <returns>Ok.</returns>
	internal async Task<ServerOutcome> DeleteAsync(ContentReference image)
	{
		await _server.Context.Seasonals.DeleteAsync(image);
		return ServerOutcome.Ok;
	}
}

/// <summary>The main-menu icon of the server, changed without a privilege check.</summary>
public readonly struct ServerIcon
{
	private readonly Server _server;

	/// <summary>Creates the view of a server.</summary>
	/// <param name="server">The server.</param>
	internal ServerIcon(Server server)
	{
		_server = server;
	}

	/// <summary>Sets the main-menu icon: its image, the address it links to, or both.</summary>
	/// <param name="name">The file name of the image, or <see langword="null" /> to keep the image.</param>
	/// <param name="image">The image, or <see langword="null" /> to keep the image.</param>
	/// <param name="url">The address the icon links to, or <see langword="null" /> for none.</param>
	/// <returns>Ok.</returns>
	/// <exception cref="ArgumentException">The name or the address is not valid.</exception>
	internal async Task<ServerOutcome> ReplaceAsync(string? name, Stream? image, Uri? url)
	{
		var settings = await _server.Context.Settings.GetAsync();
		var reference = name is not null && image is not null
			? await _server.Context.Icon.SaveAsync(name, image)
			: settings.MenuIcon.Image;
		await _server.Context.Settings.CreateOrUpdateAsync(
			settings with { MenuIcon = new ServerSettings.MenuIconSettings { Url = url, Image = reference } });
		return ServerOutcome.Ok;
	}

	/// <summary>Removes the main-menu icon.</summary>
	/// <returns>Ok.</returns>
	internal async Task<ServerOutcome> DeleteAsync()
	{
		await _server.Context.Icon.DeleteAsync();
		var settings = await _server.Context.Settings.GetAsync();
		await _server.Context.Settings.CreateOrUpdateAsync(settings with
		{
			MenuIcon = new ServerSettings.MenuIconSettings()
		});
		return ServerOutcome.Ok;
	}
}