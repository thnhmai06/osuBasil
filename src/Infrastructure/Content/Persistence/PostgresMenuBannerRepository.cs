using Basil.Application.Content.Persistence;
using Basil.Domain.Content;
using Basil.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Content.Persistence;

/// <summary>Stores the banners shown on the osu! main menu.</summary>
internal sealed class PostgresMenuBannerRepository(
	DatabaseSession database,
	ILogger<PostgresMenuBannerRepository> logger) : IMenuBannerRepository
{
	/// <inheritdoc />
	public Task CreateOrUpdateAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		var image = banner.Image.ToString();
		return database.WriteAsync(async (ContentDbContext context, CancellationToken token) =>
		{
			var stored = await context.MenuBanners.AsTracking().FirstOrDefaultAsync(row => row.Image == image, token);
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
		}, cancellationToken);
	}

	/// <inheritdoc />
	public async ValueTask<MenuBanner?> GetAsync(ContentReference image, CancellationToken cancellationToken = default)
	{
		var name = image.ToString();
		var row = await database.ReadAsync((ContentDbContext context) => context.MenuBanners
			.FirstOrDefaultAsync(item => item.Image == name, cancellationToken), cancellationToken);
		return row is null ? null : TryRead(row);
	}

	/// <inheritdoc />
	public async Task<IEnumerable<MenuBanner>> ListAsync(CancellationToken cancellationToken = default)
	{
		var rows = await database.ReadAsync((ContentDbContext context) => context.MenuBanners
			.OrderBy(row => row.CreatedAt)
			.ToListAsync(cancellationToken), cancellationToken);
		return rows.Select(TryRead).OfType<MenuBanner>().ToList();
	}

	/// <inheritdoc />
	public Task DeleteAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		var image = banner.Image.ToString();
		return database.WriteAsync((ContentDbContext context, CancellationToken token) => context.MenuBanners
			.Where(row => row.Image == image)
			.ExecuteDeleteAsync(token), cancellationToken);
	}

	/// <summary>Builds a banner from a stored row, or logs and returns null when the row is not valid.</summary>
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
		catch (Exception exception) when (exception is ArgumentException or FormatException)
		{
			logger.LogWarning(exception, "The stored menu banner {Image} is not valid and is not shown", row.Image);
			return null;
		}
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