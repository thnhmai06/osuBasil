using Basil.Application.Shared.Settings;
using Basil.Domain.Content;

namespace Basil.Application.Servers.Objects;

public sealed partial class Server
{
	public ServerFaq Faq => new(this);

	public ServerBanners Banners => new(this);

	public ServerSeasonals Seasonals => new(this);

	public ServerIcon Icon => new(this);
}

public readonly struct ServerFaq
{
	private readonly Server _server;

	internal ServerFaq(Server server)
	{
		_server = server;
	}

	internal async Task<ServerActionResult> SaveAsync(string entry, string text)
	{
		await using var writer = await _server.Context.Faqs.CreateOrUpdateAsync(entry);
		await writer.WriteAsync(text);
		writer.Commit();
		return ServerActionResult.Ok;
	}

	internal async Task<ServerActionResult> DeleteAsync(string entry)
	{
		using (var stored = await _server.Context.Faqs.ReadAsync(entry))
		{
			if (stored is null)
				return ServerActionResult.NotFound;
		}

		await _server.Context.Faqs.DeleteAsync(entry);
		return ServerActionResult.Ok;
	}
}

public readonly struct ServerBanners
{
	private readonly Server _server;

	internal ServerBanners(Server server)
	{
		_server = server;
	}

	internal async Task<(ServerActionResult Outcome, MenuBanner Banner)> CreateAsync(string name, Stream image, Uri url,
		DateTimeOffset? startsAt, DateTimeOffset? endsAt)
	{
		var reference = await _server.Context.BannerImages.SaveAsync(name, image);
		try
		{
			var banner = new MenuBanner
			{
				Image = reference,
				Url = url,
				StartsAt = startsAt,
				EndsAt = endsAt,
				CreatedAt = _server.Context.Connections.Clock.GetUtcNow()
			};
			await _server.Context.Banners.CreateOrUpdateAsync(banner);
			return (ServerActionResult.Ok, banner);
		}
		catch
		{
			await _server.Context.BannerImages.DeleteAsync(reference);
			throw;
		}
	}

	internal async Task<ServerActionResult> DeleteAsync(MenuBanner banner)
	{
		if (await _server.Context.Banners.GetAsync(banner.Image) is null) return ServerActionResult.NotFound;

		await _server.Context.Banners.DeleteAsync(banner);
		await _server.Context.BannerImages.DeleteAsync(banner.Image);
		return ServerActionResult.Ok;
	}
}

public readonly struct ServerSeasonals
{
	private readonly Server _server;

	internal ServerSeasonals(Server server)
	{
		_server = server;
	}

	internal async Task<(ServerActionResult Outcome, ContentReference Image)> CreateAsync(string name, Stream image)
	{
		return (ServerActionResult.Ok, await _server.Context.Seasonals.SaveAsync(name, image));
	}

	internal async Task<ServerActionResult> DeleteAsync(ContentReference image)
	{
		if (!(await _server.Context.Seasonals.ListAsync()).Contains(image)) return ServerActionResult.NotFound;

		await _server.Context.Seasonals.DeleteAsync(image);
		return ServerActionResult.Ok;
	}
}

public readonly struct ServerIcon
{
	private readonly Server _server;

	internal ServerIcon(Server server)
	{
		_server = server;
	}

	internal async Task<ServerActionResult> ReplaceAsync(string? name, Stream? image, Uri? url)
	{
		var settings = await _server.Context.Settings.GetAsync();
		var icon = new ServerSettings.MenuIconSettings { Url = url };
		if (name is not null && image is not null)
			icon = icon with { Image = await _server.Context.Icon.SaveAsync(name, image) };
		else
			icon = icon with { Image = settings.MenuIcon.Image };
		await _server.Context.Settings.CreateOrUpdateAsync(settings with { MenuIcon = icon });
		return ServerActionResult.Ok;
	}

	internal async Task<ServerActionResult> DeleteAsync()
	{
		var settings = await _server.Context.Settings.GetAsync();
		if (settings.MenuIcon is { Image: null, Url: null }) return ServerActionResult.NotFound;

		await _server.Context.Icon.DeleteAsync();
		await _server.Context.Settings.CreateOrUpdateAsync(settings with
		{
			MenuIcon = new ServerSettings.MenuIconSettings()
		});
		return ServerActionResult.Ok;
	}
}