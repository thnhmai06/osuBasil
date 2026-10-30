using System.Collections.Concurrent;
using Basil.Application.Content.Persistence;
using Basil.Domain.Content;
using Basil.Infrastructure.Common;
using Basil.Infrastructure.Common.Common;
using Basil.Infrastructure.Common.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Basil.Infrastructure.Content.Persistence;

/// <summary>Stores the banners shown on the osu! main menu.</summary>
internal sealed class PostgresMenuBannerRepository(
	DatabaseReader reader,
	DatabaseWriter writer,
	ILogger<PostgresMenuBannerRepository> logger)
	: IMenuBannerRepository, IResident
{
	private readonly ConcurrentDictionary<ContentReference, MenuBanner> _banners = new();
	private readonly ResidentWrites<ContentReference> _writes = new();

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		// A banner keeps the instant it was created at when it is replaced.
		var createdAt = _banners.TryGetValue(banner.Image, out var current) ? current.CreatedAt : banner.CreatedAt;
		await _writes.WriteAsync(banner.Image,
			() => writer.SaveAsync(Root.Server, (GetType(), banner.Image), Upsert(banner, createdAt)),
			() => _banners.AddOrUpdate(banner.Image, banner, (_, currentBanner) =>
			{
				if (!ReferenceEquals(currentBanner, banner))
					CopyTo(currentBanner, banner);
				return currentBanner;
			}));
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
	public async Task DeleteAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		var image = banner.Image.ToString();
		await _writes.WriteAsync(banner.Image,
			() => writer.SaveAsync(Root.Server, (GetType(), banner.Image),
				new WriteCommand<ContentDbContext>((context, token) => context
					.MenuBanners
					.Where(row => row.Image == image)
					.ExecuteDeleteAsync(token))),
			() => _banners.TryRemove(banner.Image, out _));
	}

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		var rows = await reader.ReadAsync((ContentDbContext context) => context.MenuBanners
			.ToListAsync(cancellationToken), cancellationToken);
		foreach (var row in rows)
		{
			if (!ContentReference.TryParse(row.Image, null, out var image))
			{
				logger.LogWarning("The menu banner image {Image} is not a valid asset reference and is not shown",
					row.Image);
				continue;
			}

			try
			{
				var banner = ToBanner(row, image);
				_banners[banner.Image] = banner;
			}
			catch (Exception exception) when (exception is ArgumentException or FormatException)
			{
				logger.LogWarning(exception, "The stored menu banner {Image} is not valid and is not shown", row.Image);
			}
		}
	}

	private static void CopyTo(MenuBanner live, MenuBanner from)
	{
		live.Url = from.Url;
		// Each bound is checked against the other, so the one that moves past the other's old value goes second.
		if (from.StartsAt > live.EndsAt)
		{
			live.EndsAt = from.EndsAt;
			live.StartsAt = from.StartsAt;
		}
		else
		{
			live.StartsAt = from.StartsAt;
			live.EndsAt = from.EndsAt;
		}
	}

	private static WriteCommand<ContentDbContext> Upsert(MenuBanner banner, DateTimeOffset createdAt)
	{
		var row = new MenuBannerRow
		{
			Image = banner.Image.ToString(),
			Url = banner.Url.ToString(),
			StartsAt = banner.StartsAt?.ToUniversalTime(),
			EndsAt = banner.EndsAt?.ToUniversalTime(),
			CreatedAt = createdAt.ToUniversalTime()
		};
		return new WriteCommand<ContentDbContext>((context, token) =>
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