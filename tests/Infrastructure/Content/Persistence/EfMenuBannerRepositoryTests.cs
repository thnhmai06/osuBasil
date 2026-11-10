using Basil.Application.Common;
using Basil.Application.Content.Persistence;
using Basil.Domain.Content;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Content.Tests.Persistence;

/// <summary>Contract tests of the storing of the banners shown on the main menu.</summary>
[Collection("Persistence")]
public sealed class EfMenuBannerRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset Moment = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	private IMenuBannerRepository Banners => Get<IMenuBannerRepository>();

	[Fact]
	public async Task A_listing_reads_no_more_banners_than_the_ceiling()
	{
		await StoredRows.InsertAsync(ConnectionString, "menu_banners",
			Enumerable.Range(1, ListLimits.Ceiling + 1).Select(g =>
				(IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
				{
					["image"] = $"banners/bulk{g}.png", ["url"] = "https://example.com/" + g, ["created_at"] = Moment
				}), TestContext.Current.CancellationToken);

		var listed = await Banners.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(ListLimits.Ceiling, listed.Count());
	}

	[Fact]
	public async Task A_stored_banner_is_found()
	{
		var banner = Banner("banners/one.png", Moment);

		await Banners.CreateOrUpdateAsync(banner);

		Assert.NotNull(await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_banner_is_found_by_its_image()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);

		var found = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(banner.Url, found.Url);
	}

	[Fact]
	public async Task A_banner_is_stored_when_storing_it_returns()
	{
		var banner = Banner("banners/one.png", Moment);

		await Banners.CreateOrUpdateAsync(banner);

		var rows = await StoredRows.ReadAsync(ConnectionString,
			"select image, url, created_at from menu_banners where image = @image",
			new Dictionary<string, object?> { ["@image"] = banner.Image.ToString() },
			TestContext.Current.CancellationToken);

		var stored = Assert.Single(rows);
		Assert.Equal("banners/one.png", stored.Text("image"));
		Assert.Equal(banner.Url.ToString(), stored.Text("url"));
		Assert.Equal(Moment, stored.Instant("created_at"));
	}

	[Fact]
	public async Task The_banners_are_listed_by_the_instant_they_were_created_at()
	{
		var first = Banner("banners/one.png", Moment);
		var second = Banner("banners/two.png", Moment.AddMinutes(1));
		await Banners.CreateOrUpdateAsync(second);
		await Banners.CreateOrUpdateAsync(first);

		var banners = (await Banners.ListAsync(TestContext.Current.CancellationToken)).ToList();

		Assert.Equal(new[] { first.Image, second.Image }, banners.Select(banner => banner.Image));
	}

	[Fact]
	public async Task A_stored_banner_is_read_back_from_the_database()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);

		var reloaded = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);

		Assert.NotNull(reloaded);
		Assert.Equal(banner.Url, reloaded.Url);
		Assert.Equal(banner.CreatedAt, reloaded.CreatedAt);
	}

	[Fact]
	public async Task A_banner_is_deleted()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);

		await Banners.DeleteAsync(banner);

		Assert.Null(await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task A_deleted_banner_is_gone_from_the_database_when_deleting_it_returns()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);

		await Banners.DeleteAsync(banner);

		var rows = await StoredRows.ReadAsync(ConnectionString,
			"select image from menu_banners where image = @image",
			new Dictionary<string, object?> { ["@image"] = banner.Image.ToString() },
			TestContext.Current.CancellationToken);

		Assert.Empty(rows);
	}

	[Fact]
	public async Task A_banner_is_replaced()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);
		banner.Url = new Uri("https://example.com/changed");

		await Banners.CreateOrUpdateAsync(banner);

		var found = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);
		Assert.Equal(new Uri("https://example.com/changed"), found!.Url);
	}

	[Fact]
	public async Task A_replaced_banner_is_stored_when_storing_it_returns()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);
		banner.Url = new Uri("https://example.com/changed");

		await Banners.CreateOrUpdateAsync(banner);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select url from menu_banners where image = @image",
			new Dictionary<string, object?> { ["@image"] = banner.Image.ToString() },
			TestContext.Current.CancellationToken);

		Assert.Equal("https://example.com/changed", Assert.Single(rows).Text("url"));
	}

	[Fact]
	public async Task A_replaced_banner_keeps_the_instant_it_was_created_at()
	{
		var created = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(created);
		await Banners.CreateOrUpdateAsync(new MenuBanner
		{
			Image = created.Image,
			Url = new Uri("https://example.com/changed"),
			StartsAt = null,
			EndsAt = null,
			CreatedAt = Moment.AddDays(1)
		});

		var reloaded = await Banners.GetAsync(created.Image, TestContext.Current.CancellationToken);

		Assert.NotNull(reloaded);
		Assert.Equal(created.CreatedAt, reloaded.CreatedAt);
	}

	[Fact]
	public async Task Of_two_concurrent_changes_of_a_banner_the_one_stored_last_is_read()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);
		var first = Banner("banners/one.png", Moment);
		first.Url = new Uri("https://example.com/first");
		var second = Banner("banners/one.png", Moment);
		second.Url = new Uri("https://example.com/second");

		var changes = new[]
		{
			Banners.CreateOrUpdateAsync(first),
			Banners.CreateOrUpdateAsync(second)
		};
		await Task.WhenAll(changes);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select url from menu_banners where image = @image",
			new Dictionary<string, object?> { ["@image"] = banner.Image.ToString() },
			TestContext.Current.CancellationToken);
		var found = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);
		Assert.Equal(Assert.Single(rows).Text("url"), found!.Url.ToString());
	}

	/// <summary>Builds a banner of the main menu.</summary>
	/// <param name="image">The path of the image the banner shows.</param>
	/// <param name="createdAt">The instant the banner was created at.</param>
	[Fact]
	public async Task A_banner_can_be_moved_to_a_window_that_starts_after_the_old_one_ended()
	{
		var banner = Banner("banners/moving.png", Moment);
		banner.StartsAt = Moment;
		banner.EndsAt = Moment.AddDays(1);
		await Banners.CreateOrUpdateAsync(banner);
		var moved = Banner("banners/moving.png", Moment);
		moved.StartsAt = Moment.AddDays(10);
		moved.EndsAt = Moment.AddDays(11);

		await Banners.CreateOrUpdateAsync(moved);

		var found = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);
		Assert.Equal((Moment.AddDays(10), Moment.AddDays(11)), (found!.StartsAt, found.EndsAt));
	}

	[Fact]
	public async Task A_banner_can_be_moved_to_a_window_that_ends_before_the_old_one_started()
	{
		var banner = Banner("banners/moving.png", Moment);
		banner.StartsAt = Moment.AddDays(10);
		banner.EndsAt = Moment.AddDays(11);
		await Banners.CreateOrUpdateAsync(banner);
		var moved = Banner("banners/moving.png", Moment);
		moved.StartsAt = Moment;
		moved.EndsAt = Moment.AddDays(1);

		await Banners.CreateOrUpdateAsync(moved);

		var found = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);
		Assert.Equal((Moment, Moment.AddDays(1)), (found!.StartsAt, found.EndsAt));
	}

	[Theory]
	[InlineData("banners/bad_url.png", "http://[invalid")]
	[InlineData("../escape.png", "https://example.com/banner")]
	public async Task A_stored_banner_that_is_not_valid_is_left_out_without_stopping_the_others(string image,
		string url)
	{
		var good = Banner("banners/good.png", Moment);
		await Banners.CreateOrUpdateAsync(good);
		await StoredRows.InsertAsync(ConnectionString, "menu_banners", [
			new Dictionary<string, object?> { ["image"] = image, ["url"] = url, ["created_at"] = Moment }
		], TestContext.Current.CancellationToken);

		var listed = await Banners.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal([good.Image], listed.Select(banner => banner.Image));
	}

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
}