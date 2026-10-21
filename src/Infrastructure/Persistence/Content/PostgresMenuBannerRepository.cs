using System.Collections.Concurrent;
using Basil.Application.Persistence.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Persistence.Common;
using Basil.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Persistence.Content;

/// <summary>Stores the banners shown on the osu! main menu.</summary>
internal sealed class PostgresMenuBannerRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	ILogger<PostgresMenuBannerRepository> logger)
	: IMenuBannerRepository, IResident
{
	private readonly ConcurrentDictionary<ContentReference, MenuBanner> _banners = new();

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		var live = _banners.AddOrUpdate(banner.Image, banner, (_, current) =>
		{
			if (!ReferenceEquals(current, banner))
				CopyTo(current, banner);
			return current;
		});
		_ = writer.SaveAsync(Root.Server, (GetType(), live.Image), Upsert(live));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public ValueTask<MenuBanner?> GetAsync(ContentReference image, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(_banners.GetValueOrDefault(image));
	}

	/// <inheritdoc />
	public Task<IEnumerable<MenuBanner>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IEnumerable<MenuBanner>>(_banners.Values.OrderBy(banner => banner.CreatedAt));
	}

	/// <inheritdoc />
	public Task DeleteAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		_banners.TryRemove(banner.Image, out _);
		var image = banner.Image.ToString();
		_ = writer.SaveAsync(Root.Server, (GetType(), banner.Image), new WriteCommand((context, token) => context.MenuBanners
			.Where(row => row.Image == image)
			.ExecuteDeleteAsync(token)));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		var rows = await reader.ReadAsync(context => context.MenuBanners
			.ToListAsync(cancellationToken), cancellationToken);
		foreach (var row in rows)
		{
			if (!ContentReference.TryParse(row.Image, null, out var image))
			{
				logger.LogWarning("The menu banner image {Image} is not a valid asset reference and is not shown",
					row.Image);
				continue;
			}

			var banner = ToBanner(row, image);
			_banners[banner.Image] = banner;
		}
	}

	private static void CopyTo(MenuBanner live, MenuBanner from)
	{
		live.Url = from.Url;
		live.StartsAt = from.StartsAt;
		live.EndsAt = from.EndsAt;
	}

	private static WriteCommand Upsert(MenuBanner banner)
	{
		var row = new MenuBannerRow
		{
			Image = banner.Image.ToString(),
			Url = banner.Url.ToString(),
			StartsAt = banner.StartsAt?.ToUniversalTime(),
			EndsAt = banner.EndsAt?.ToUniversalTime(),
			CreatedAt = banner.CreatedAt.ToUniversalTime()
		};
		return new WriteCommand((context, token) =>
			context.UpsertAsync(row, stored => stored.Image == row.Image, token));
	}

	/// <summary>Builds a banner from a stored row and its parsed image.</summary>
	private static MenuBanner ToBanner(MenuBannerRow row, ContentReference image)
	{
		return new MenuBanner
		{
			Image = image,
			Url = new Uri(row.Url, UriKind.RelativeOrAbsolute),
			StartsAt = row.StartsAt,
			EndsAt = row.EndsAt,
			CreatedAt = row.CreatedAt
		};
	}
}