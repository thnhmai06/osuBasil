using System.Collections.Concurrent;
using Basil.Application.Storage.Contracts.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Storage.Writing;
using Dapper;

namespace Basil.Infrastructure.Storage.Content;

/// <summary>Stores the banners shown on the osu! main menu.</summary>
internal sealed class PostgresMenuBannerRepository(Database database, DatabaseWriter writer)
	: IMenuBannerRepository, IResident
{
	private readonly ConcurrentDictionary<Uri, MenuBanner> _banners = new();

	/// <inheritdoc />
	public async Task LoadAsync(CancellationToken cancellationToken)
	{
		var rows = await database.ReadAsync(connection => connection.QueryAsync<MenuBannerRow>(
			"select image, url, starts_at, ends_at, created_at from menu_banners"), cancellationToken);
		foreach (var row in rows)
		{
			var banner = ToBanner(row);
			_banners[banner.Image] = banner;
		}
	}

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
	public ValueTask<MenuBanner?> GetAsync(Uri image, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(_banners.GetValueOrDefault(image));

	/// <inheritdoc />
	public Task<IReadOnlyList<MenuBanner>> ListAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<IReadOnlyList<MenuBanner>>([.. _banners.Values.OrderBy(banner => banner.CreatedAt)]);
	}

	/// <inheritdoc />
	public Task DeleteAsync(MenuBanner banner, CancellationToken cancellationToken = default)
	{
		_banners.TryRemove(banner.Image, out _);
		_ = writer.SaveAsync(Root.Server, (GetType(), banner.Image), new WriteCommand(
			"delete from menu_banners where image = @Image",
			new { Image = banner.Image.ToString() }));
		return Task.CompletedTask;
	}

	private static void CopyTo(MenuBanner live, MenuBanner from)
	{
		live.Url = from.Url;
		live.StartsAt = from.StartsAt;
		live.EndsAt = from.EndsAt;
	}

	private static WriteCommand Upsert(MenuBanner banner)
	{
		var url = banner.Url.ToString();
		var startsAt = banner.StartsAt?.ToUniversalTime();
		var endsAt = banner.EndsAt?.ToUniversalTime();
		var createdAt = banner.CreatedAt.ToUniversalTime();
		return new WriteCommand(
			"""
			insert into menu_banners (image, url, starts_at, ends_at, created_at)
			values (@Image, @Url, @StartsAt, @EndsAt, @CreatedAt)
			on conflict (image) do update set
				url = excluded.url,
				starts_at = excluded.starts_at,
				ends_at = excluded.ends_at,
				created_at = excluded.created_at;
			""",
			new
			{
				Image = banner.Image.ToString(), Url = url, StartsAt = startsAt, EndsAt = endsAt, CreatedAt = createdAt
			});
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