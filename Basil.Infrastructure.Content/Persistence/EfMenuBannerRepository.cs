using Basil.Domain;
using Basil.Application.Content.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Content;
using Basil.Infrastructure.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Content.Persistence;

internal sealed class EfMenuBannerRepository(
	DatabaseSession database,
	ILogger<EfMenuBannerRepository> logger) : IMenuBannerRepository
{
	public Task CreateOrUpdateAsync(MenuBanner banner)
	{
		var image = banner.Image.ToString();
		return database.WriteAsync(async (ContentDbContext context) =>
		{
			var stored = await context.MenuBanners.AsTracking().FirstOrDefaultAsync(row => row.Image == image);
			if (stored is null)
			{
				context.MenuBanners.Add(new MenuBannerRow
				{
					Image = image,
					Url = banner.Url.ToString(),
					StartsAt = banner.StartsAt?.ToUniversalTime(),
					EndsAt = banner.EndsAt?.ToUniversalTime(),
					CreatedAt = banner.CreatedAt.ToUniversalTime()
				});
				return;
			}

			// A banner keeps the instant it was created at when it is replaced.
			stored.Url = banner.Url.ToString();
			stored.StartsAt = banner.StartsAt?.ToUniversalTime();
			stored.EndsAt = banner.EndsAt?.ToUniversalTime();
		});
	}

	public async ValueTask<MenuBanner?> GetAsync(ContentReference image, CancellationToken ct = default)
	{
		var name = image.ToString();
		var row = await database.ReadAsync((ContentDbContext context) => context.MenuBanners
			.FirstOrDefaultAsync(item => item.Image == name, ct), ct);
		return row is null ? null : TryRead(row);
	}

	public async Task<IEnumerable<MenuBanner>> ListAsync(CancellationToken ct = default)
	{
		var rows = await database.ReadAsync((ContentDbContext context) => context.MenuBanners
			.OrderBy(row => row.CreatedAt)
			.Take(ListLimits.Ceiling)
			.ToListAsync(ct), ct);
		return rows.Select(TryRead).OfType<MenuBanner>().ToList();
	}

	public Task DeleteAsync(MenuBanner banner)
	{
		var image = banner.Image.ToString();
		return database.WriteAsync((ContentDbContext context) => context.MenuBanners
			.Where(row => row.Image == image)
			.ExecuteDeleteAsync());
	}

	private MenuBanner? TryRead(MenuBannerRow row)
	{
		if (!ContentReference.TryParse(row.Image, null, out var image))
		{
			logger.LogWarning("The menu banner image {Image} is not a valid asset reference and is not shown",
				row.Image);
			return null;
		}

		try
		{
			return ToBanner(row, image);
		}
		catch (Exception exception) when (exception is ArgumentException or FormatException or BasilException)
		{
			logger.LogWarning(exception, "The stored menu banner {Image} is not valid and is not shown", row.Image);
			return null;
		}
	}

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