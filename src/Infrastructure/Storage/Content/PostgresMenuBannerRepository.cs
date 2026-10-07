using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Storage.Caching;
using Basil.Infrastructure.Storage.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Stores the banners shown on the osu! main menu.</summary>
internal sealed class PostgresMenuBannerRepository(Database database, DatabaseWriter writer)
	: CachedRepository<Uri, MenuBanner>(database, writer), IMenuBannerRepository
{
	private volatile bool _listed;

	protected override Uri KeyOf(MenuBanner item) => item.Image;

	protected override Root RootOf(MenuBanner item) => Root.Server;

	/// <inheritdoc />
	public Task CreateOrUpdateAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		var live = Track(banner);
		if (!ReferenceEquals(live, banner))
			Update(live, banner);
		_ = SaveAsync(live);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public ValueTask<MenuBanner?> GetAsync(Uri image, CancellationToken cancellationToken = default) => FindAsync(image, cancellationToken);

	/// <inheritdoc />
	public async Task<IReadOnlyList<MenuBanner>> ListAsync(CancellationToken cancellationToken = default)
	{
		if (!_listed)
		{
			var rows = await Database.ReadAsync(connection => connection.QueryAsync<MenuBannerRow>(
				"select image, url, starts_at, ends_at, created_at from menu_banners"), cancellationToken);
			foreach (var row in rows)
				Track(ToBanner(row));
			_listed = true;
		}

		return [.. Items.Values.OrderBy(banner => banner.CreatedAt)];
	}

	/// <inheritdoc />
	public Task DeleteAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		_ = RemoveAsync(banner);
		return Task.CompletedTask;
	}

	protected override async Task<MenuBanner?> LoadAsync(Uri key, CancellationToken cancellationToken)
	{
		var row = await Database.ReadAsync(connection => connection.QuerySingleOrDefaultAsync<MenuBannerRow>(
			"select image, url, starts_at, ends_at, created_at from menu_banners where image = @Image",
			new { Image = key.ToString() }), cancellationToken);
		return row is null ? null : ToBanner(row);
	}

	protected override string WriteSql =>
		"""
		insert into menu_banners (image, url, starts_at, ends_at, created_at)
		values (@Image, @Url, @StartsAt, @EndsAt, @CreatedAt)
		on conflict (image) do update set
			url = excluded.url,
			starts_at = excluded.starts_at,
			ends_at = excluded.ends_at,
			created_at = excluded.created_at;
		""";

	protected override object WriteParameters(MenuBanner banner)
	{
		var url = banner.Url.ToString();
		var startsAt = banner.StartsAt?.ToUniversalTime();
		var endsAt = banner.EndsAt?.ToUniversalTime();
		var createdAt = banner.CreatedAt.ToUniversalTime();
		return new { Image = banner.Image.ToString(), Url = url, StartsAt = startsAt, EndsAt = endsAt, CreatedAt = createdAt };
	}

	protected override WriteCommand EraseCommand(Uri key)
	{
		return new WriteCommand("delete from menu_banners where image = @Image",
			new { Image = key.ToString() });
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
			StartsAt = row.StartsAt,
			EndsAt = row.EndsAt,
			CreatedAt = row.CreatedAt
		};
	}

	/// <summary>A stored row of the <c>menu_banners</c> table.</summary>
	private sealed class MenuBannerRow
	{
		public string Image { get; set; } = "";
		public string Url { get; set; } = "";
		public DateTimeOffset? StartsAt { get; set; }
		public DateTimeOffset? EndsAt { get; set; }
		public DateTimeOffset CreatedAt { get; set; }
	}
}
