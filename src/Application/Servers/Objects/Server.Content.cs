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

	internal async Task<ServerOutcome> SaveAsync(string entry, string text)
	{
		await using var writer = await _server.Context.Faqs.CreateOrUpdateAsync(entry);
		await writer.WriteAsync(text);
		writer.Commit();
		return ServerOutcome.Ok;
	}

	internal async Task<ServerOutcome> DeleteAsync(string entry)
	{
		using (var stored = await _server.Context.Faqs.ReadAsync(entry))
			if (stored is null)
				return ServerOutcome.NotFound;

		await _server.Context.Faqs.DeleteAsync(entry);
		return ServerOutcome.Ok;
	}
}

public readonly struct ServerBanners
{
	private readonly Server _server;

	internal ServerBanners(Server server)
	{
		_server = server;
	}

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

	internal async Task<ServerOutcome> DeleteAsync(MenuBanner banner)
	{
		if (await _server.Context.Banners.GetAsync(banner.Image) is null) return ServerOutcome.NotFound;

		await _server.Context.Banners.DeleteAsync(banner);
		await _server.Context.BannerImages.DeleteAsync(banner.Image);
		return ServerOutcome.Ok;
	}
}

public readonly struct ServerSeasonals
{
	private readonly Server _server;

	internal ServerSeasonals(Server server)
	{
		_server = server;
	}

	internal async Task<(ServerOutcome Outcome, ContentReference Image)> CreateAsync(string name, Stream image)
	{
		return (ServerOutcome.Ok, await _server.Context.Seasonals.SaveAsync(name, image));
	}

	internal async Task<ServerOutcome> DeleteAsync(ContentReference image)
	{
		if (!(await _server.Context.Seasonals.ListAsync()).Contains(image)) return ServerOutcome.NotFound;

		await _server.Context.Seasonals.DeleteAsync(image);
		return ServerOutcome.Ok;
	}
}

public readonly struct ServerIcon
{
	private readonly Server _server;

	internal ServerIcon(Server server)
	{
		_server = server;
	}

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

	internal async Task<ServerOutcome> DeleteAsync()
	{
		var settings = await _server.Context.Settings.GetAsync();
		if (settings.MenuIcon is { Image: null, Url: null }) return ServerOutcome.NotFound;

		await _server.Context.Icon.DeleteAsync();
		await _server.Context.Settings.CreateOrUpdateAsync(settings with
		{
			MenuIcon = new ServerSettings.MenuIconSettings()
		});
		return ServerOutcome.Ok;
	}
}