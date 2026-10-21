using Basil.Application.Persistence.Content;
using Basil.Domain.Content;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Content;

/// <summary>Contract tests of the storing of the banners shown on the main menu.</summary>
[Collection("Persistence")]
public sealed class PostgresMenuBannerRepositoryTests : DatabaseTests
{
	private static readonly DateTimeOffset Moment = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	public PostgresMenuBannerRepositoryTests(PersistenceFixture container) : base(container) { }

	private IMenuBannerRepository Banners => Get<IMenuBannerRepository>();

	[Fact]
	public async Task A_banner_is_stored()
	{
		var banner = Banner("banners/one.png", Moment);

		await Banners.CreateOrUpdateAsync(banner, TestContext.Current.CancellationToken);

		Assert.NotNull(await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_banner_is_found_by_its_image()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner, TestContext.Current.CancellationToken);

		var found = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(banner.Url, found.Url);
	}

	[Fact]
	public async Task A_banner_is_written_to_the_database()
	{
		var banner = Banner("banners/one.png", Moment);

		await Banners.CreateOrUpdateAsync(banner, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select image, url, created_at from menu_banners where image = @image",
			new Dictionary<string, object?> { ["@image"] = banner.Image.ToString() },
			rows => rows.Count == 1, "the stored banner", TestContext.Current.CancellationToken);

		Assert.Equal("banners/one.png", rows[0].Text("image"));
		Assert.Equal(banner.Url.ToString(), rows[0].Text("url"));
		Assert.Equal(Moment, rows[0].Instant("created_at"));
	}

	[Fact]
	public async Task The_banners_are_listed_by_the_instant_they_were_created_at()
	{
		var first = Banner("banners/one.png", Moment);
		var second = Banner("banners/two.png", Moment.AddMinutes(1));
		await Banners.CreateOrUpdateAsync(second, TestContext.Current.CancellationToken);
		await Banners.CreateOrUpdateAsync(first, TestContext.Current.CancellationToken);

		var banners = (await Banners.ListAsync(TestContext.Current.CancellationToken)).ToList();

		Assert.Equal(new[] { first.Image, second.Image }, banners.Select(banner => banner.Image));
	}

	[Fact]
	public async Task A_stored_banner_is_reloaded()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner, TestContext.Current.CancellationToken);
		await WaitForBannerAsync(banner.Image);

		var reloaded = await ReloadAsync(banner.Image);

		Assert.NotNull(reloaded);
		Assert.Equal(banner.Url, reloaded.Url);
		Assert.Equal(banner.CreatedAt, reloaded.CreatedAt);
	}

	[Fact]
	public async Task A_banner_is_deleted()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner, TestContext.Current.CancellationToken);

		await Banners.DeleteAsync(banner, TestContext.Current.CancellationToken);

		Assert.Null(await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_deleted_banner_is_written_to_the_database_as_gone()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner, TestContext.Current.CancellationToken);
		await WaitForBannerAsync(banner.Image);

		await Banners.DeleteAsync(banner, TestContext.Current.CancellationToken);

		await StoredRows.WaitForAsync(ConnectionString,
			"select image from menu_banners where image = @image",
			new Dictionary<string, object?> { ["@image"] = banner.Image.ToString() },
			rows => rows.Count == 0, "the deleted banner", TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task A_banner_is_replaced()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner, TestContext.Current.CancellationToken);
		banner.Url = new Uri("https://example.com/changed");

		await Banners.CreateOrUpdateAsync(banner, TestContext.Current.CancellationToken);

		var found = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);
		Assert.Equal(new Uri("https://example.com/changed"), found!.Url);
	}

	[Fact]
	public async Task A_replaced_banner_is_written_to_the_database()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner, TestContext.Current.CancellationToken);
		banner.Url = new Uri("https://example.com/changed");

		await Banners.CreateOrUpdateAsync(banner, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select url from menu_banners where image = @image",
			new Dictionary<string, object?> { ["@image"] = banner.Image.ToString() },
			rows => rows.Count == 1 && rows[0].Text("url") == "https://example.com/changed",
			"the replaced banner", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_replaced_banner_keeps_the_instant_it_was_created_at()
	{
		var created = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(created, TestContext.Current.CancellationToken);
		await Banners.CreateOrUpdateAsync(new MenuBanner
		{
			Image = created.Image,
			Url = new Uri("https://example.com/changed"),
			StartsAt = null,
			EndsAt = null,
			CreatedAt = Moment.AddDays(1)
		}, TestContext.Current.CancellationToken);
		await WaitForBannerAsync(created.Image);

		var reloaded = await ReloadAsync(created.Image);

		Assert.NotNull(reloaded);
		Assert.Equal(created.CreatedAt, reloaded.CreatedAt);
	}

	/// <summary>Builds a banner of the main menu.</summary>
	/// <param name="image">The path of the image the banner shows.</param>
	/// <param name="createdAt">The instant the banner was created at.</param>
	private static MenuBanner Banner(string image, DateTimeOffset createdAt)
	{
		return new MenuBanner
		{
			Image = new ContentReference(image),
			Url = new Uri("https://example.com/banner"),
			StartsAt = null,
			EndsAt = null,
			CreatedAt = createdAt
		};
	}

	/// <summary>Reads a banner as a fresh server would read it.</summary>
	/// <param name="image">The image of the banner to read.</param>
	/// <returns>The banner the fresh server loaded from the database.</returns>
	private async Task<MenuBanner?> ReloadAsync(ContentReference image)
	{
		await using var fresh = new StorageComposition(ConnectionString);
		var repository = fresh.Get<IMenuBannerRepository>();
		await fresh.ResidentOf(repository).LoadAsync(TestContext.Current.CancellationToken);
		return await repository.GetAsync(image, TestContext.Current.CancellationToken);
	}

	/// <summary>Waits until a banner is stored.</summary>
	/// <param name="image">The image of the banner to wait for.</param>
	private async Task WaitForBannerAsync(ContentReference image)
	{
		await StoredRows.WaitForAsync(ConnectionString, "select image from menu_banners where image = @image",
			new Dictionary<string, object?> { ["@image"] = image.ToString() }, rows => rows.Count == 1,
			"the stored banner", TestContext.Current.CancellationToken);
	}
}
