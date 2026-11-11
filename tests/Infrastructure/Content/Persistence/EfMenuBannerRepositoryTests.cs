using Basil.Application.Shared;
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
	public async Task Listing_RespectsCeiling()
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
	public async Task StoredBanner_Found()
	{
		var banner = Banner("banners/one.png", Moment);

		await Banners.CreateOrUpdateAsync(banner);

		Assert.NotNull(await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task BannerByImage_Found()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);

		var found = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);

		Assert.NotNull(found);
		Assert.Equal(banner.Url, found.Url);
	}

	[Fact]
	public async Task StoredBanner_VisibleOnReturn()
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
	public async Task Listing_ByCreationInstant()
	{
		var first = Banner("banners/one.png", Moment);
		var second = Banner("banners/two.png", Moment.AddMinutes(1));
		await Banners.CreateOrUpdateAsync(second);
		await Banners.CreateOrUpdateAsync(first);

		var banners = (await Banners.ListAsync(TestContext.Current.CancellationToken)).ToList();

		Assert.Equal(new[] { first.Image, second.Image }, banners.Select(banner => banner.Image));
	}

	[Fact]
	public async Task StoredBanner_ReadBack()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);

		var reloaded = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);

		Assert.NotNull(reloaded);
		Assert.Equal(banner.Url, reloaded.Url);
		Assert.Equal(banner.CreatedAt, reloaded.CreatedAt);
	}

	[Fact]
	public async Task ExistingBanner_Deleted()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);

		await Banners.DeleteAsync(banner);

		Assert.Null(await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task DeletedBanner_GoneOnReturn()
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
	public async Task ExistingBanner_Replaced()
	{
		var banner = Banner("banners/one.png", Moment);
		await Banners.CreateOrUpdateAsync(banner);
		banner.Url = new Uri("https://example.com/changed");

		await Banners.CreateOrUpdateAsync(banner);

		var found = await Banners.GetAsync(banner.Image, TestContext.Current.CancellationToken);
		Assert.Equal(new Uri("https://example.com/changed"), found!.Url);
	}

	[Fact]
	public async Task ReplacedBanner_StoredOnReturn()
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
	public async Task ReplacedBanner_KeepsCreationInstant()
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
	public async Task ConcurrentBannerChanges_LastStoredWins()
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
	public async Task LaterWindow_MoveAllowed()
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
	public async Task EarlierWindow_MoveAllowed()
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
	public async Task InvalidStoredBanner_Skipped(string image,
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