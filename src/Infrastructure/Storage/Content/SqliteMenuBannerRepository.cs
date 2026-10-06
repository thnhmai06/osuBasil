using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Content;
using Dapper;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Stores the banners shown on the osu! main menu.</summary>
internal sealed class SqliteMenuBannerRepository(Database database) : IMenuBannerRepository
{
	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"""
			INSERT INTO MenuBanners (Image, Url, StartsAt, EndsAt, CreatedAt)
			VALUES (@Image, @Url, @StartsAt, @EndsAt, @CreatedAt)
			ON CONFLICT(Image) DO UPDATE SET
				Url = excluded.Url,
				StartsAt = excluded.StartsAt,
				EndsAt = excluded.EndsAt,
				CreatedAt = excluded.CreatedAt;
			""",
			new
			{
				Image = banner.Image.ToString(),
				Url = banner.Url.ToString(),
				StartsAt = banner.StartsAt?.ToUnixTimeMilliseconds(),
				EndsAt = banner.EndsAt?.ToUnixTimeMilliseconds(),
				CreatedAt = banner.CreatedAt.ToUnixTimeMilliseconds()
			});
	}

	/// <inheritdoc />
	public async ValueTask<MenuBanner?> GetAsync(Uri image, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var row = await connection.QuerySingleOrDefaultAsync<MenuBannerRow>(
			"SELECT Image, Url, StartsAt, EndsAt, CreatedAt FROM MenuBanners WHERE Image = @Image",
			new { Image = image.ToString() });

		return row is null ? null : ToBanner(row);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<MenuBanner>> ListAsync(CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		var rows = await connection.QueryAsync<MenuBannerRow>(
			"SELECT Image, Url, StartsAt, EndsAt, CreatedAt FROM MenuBanners ORDER BY CreatedAt");

		return [.. rows.Select(ToBanner)];
	}

	/// <inheritdoc />
	public async Task DeleteAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		await using var connection = await database.OpenAsync(cancellationToken);
		await connection.ExecuteAsync(
			"DELETE FROM MenuBanners WHERE Image = @Image",
			new { Image = banner.Image.ToString() });
	}

	/// <summary>Builds a banner from a stored row.</summary>
	private static MenuBanner ToBanner(MenuBannerRow row)
	{
		return new MenuBanner
		{
			Image = new Uri(row.Image, UriKind.RelativeOrAbsolute),
			Url = new Uri(row.Url, UriKind.RelativeOrAbsolute),
			StartsAt = row.StartsAt is { } startsAt ? DateTimeOffset.FromUnixTimeMilliseconds(startsAt) : null,
			EndsAt = row.EndsAt is { } endsAt ? DateTimeOffset.FromUnixTimeMilliseconds(endsAt) : null,
			CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt)
		};
	}

	/// <summary>A stored row of the MenuBanners table.</summary>
	private sealed class MenuBannerRow
	{
		public string Image { get; set; } = "";
		public string Url { get; set; } = "";
		public long? StartsAt { get; set; }
		public long? EndsAt { get; set; }
		public long CreatedAt { get; set; }
	}
}
