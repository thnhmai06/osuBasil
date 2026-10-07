using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Storage.Caching;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Stores the banners shown on the osu! main menu.</summary>
internal sealed class SqliteMenuBannerRepository(Database database, WriteBuffer buffer)
	: CachedRepository<Uri, MenuBanner>(database, buffer), IMenuBannerRepository
{
	private volatile bool _listed;

	protected override Uri KeyOf(MenuBanner item) => item.Image;

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		var live = Track(banner);
		if (!ReferenceEquals(live, banner))
			Update(live, banner);
		Save(live);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public ValueTask<MenuBanner?> GetAsync(Uri image, CancellationToken cancellationToken = default) => FindAsync(image, cancellationToken);

	/// <inheritdoc />
	public async Task<IReadOnlyList<MenuBanner>> ListAsync(CancellationToken cancellationToken = default)
	{
		if (!_listed)
		{
			await using var connection = await OpenAsync(cancellationToken);
			foreach (var row in await connection.QueryAsync<MenuBannerRow>(
				         "SELECT Image, Url, StartsAt, EndsAt, CreatedAt FROM MenuBanners"))
				Track(ToBanner(row));
			_listed = true;
		}

		return [.. Items.Values.OrderBy(banner => banner.CreatedAt)];
	}

	/// <inheritdoc />
	public Task DeleteAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		Remove(banner);
		return Task.CompletedTask;
	}

	protected override async Task<MenuBanner?> ReadAsync(SqliteConnection connection, Uri key,
		CancellationToken cancellationToken)
	{
		var row = await connection.QuerySingleOrDefaultAsync<MenuBannerRow>(
			"SELECT Image, Url, StartsAt, EndsAt, CreatedAt FROM MenuBanners WHERE Image = @Image",
			new { Image = key.ToString() });
		return row is null ? null : ToBanner(row);
	}

	protected override Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction, MenuBanner banner)
	{
		var url = banner.Url.ToString();
		var startsAt = banner.StartsAt?.ToUnixTimeMilliseconds();
		var endsAt = banner.EndsAt?.ToUnixTimeMilliseconds();
		var createdAt = banner.CreatedAt.ToUnixTimeMilliseconds();
		return connection.ExecuteAsync(
			"""
			INSERT INTO MenuBanners (Image, Url, StartsAt, EndsAt, CreatedAt)
			VALUES (@Image, @Url, @StartsAt, @EndsAt, @CreatedAt)
			ON CONFLICT(Image) DO UPDATE SET
				Url = excluded.Url,
				StartsAt = excluded.StartsAt,
				EndsAt = excluded.EndsAt,
				CreatedAt = excluded.CreatedAt;
			""",
			new { Image = banner.Image.ToString(), Url = url, StartsAt = startsAt, EndsAt = endsAt, CreatedAt = createdAt },
			transaction);
	}

	protected override Task EraseAsync(SqliteConnection connection, SqliteTransaction transaction, Uri key)
	{
		return connection.ExecuteAsync("DELETE FROM MenuBanners WHERE Image = @Image",
			new { Image = key.ToString() }, transaction);
	}

	private static void Update(MenuBanner live, MenuBanner update)
	{
		live.Url = update.Url;
		live.StartsAt = update.StartsAt;
		live.EndsAt = update.EndsAt;
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
