using Basil.Application.Content.Persistence;
using Basil.Application.Content.Storage;
using Basil.Application.Servers.Capabilities;
using Basil.Application.Servers.Objects;
using Basil.Domain.Content;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Application.Servers.Tests.Capabilities;

/// <summary>Adding a main-menu banner leaves nothing behind when it cannot be stored.</summary>
public sealed class ScopedServerContentTests
{
	[Fact]
	public async Task BannerStoreFails_DeletesImageRethrows()
	{
		var image = new ContentReference("banners/a.png");
		var images = Substitute.For<IMenuBannerStorage>();
		images.SaveAsync("a.png", Arg.Any<Stream>()).Returns(image);
		var banners = Substitute.For<IMenuBannerRepository>();
		banners.CreateOrUpdateAsync(Arg.Any<MenuBanner>())
			.Returns(Task.FromException(new IOException("disk full")));
		using var app = new TestApplication(services =>
		{
			services.AddSingleton(images);
			services.AddSingleton(banners);
		});
		var manager = await RoomFixture.PlayerAsync(app, "Boss",
			UserPrivilegesExtensions.Default | UserPrivileges.Owner);

		await Assert.ThrowsAsync<IOException>(() => app.Server.ScopeTo(manager).Banners.CreateAsync("a.png",
			new MemoryStream(), new Uri("https://example.org"), null, null));

		await images.Received(1).DeleteAsync(image);
	}

	private static async Task<ScopedServer> OwnerAsync(TestApplication app)
	{
		var owner = await RoomFixture.PlayerAsync(app, "Owner", UserPrivilegesExtensions.Default | UserPrivileges.Owner);
		return app.Server.ScopeTo(owner);
	}

	[Fact]
	public async Task FaqDeleteMissing_NotFound()
	{
		var faqs = Substitute.For<IFaqStorage>();
		faqs.ReadAsync("gone", Arg.Any<CancellationToken>()).Returns((StreamReader?)null);
		using var app = new TestApplication(services => services.AddSingleton(faqs));

		var result = await (await OwnerAsync(app)).Faq.DeleteAsync("gone");

		Assert.Equal(ServerOutcome.NotFound, result);
		await faqs.DidNotReceiveWithAnyArgs().DeleteAsync(default!);
	}

	[Fact]
	public async Task FaqDeleteExisting_Removed()
	{
		var faqs = Substitute.For<IFaqStorage>();
		faqs.ReadAsync("here", Arg.Any<CancellationToken>()).Returns(new StreamReader(new MemoryStream()));
		using var app = new TestApplication(services => services.AddSingleton(faqs));

		var result = await (await OwnerAsync(app)).Faq.DeleteAsync("here");

		Assert.Equal(ServerOutcome.Ok, result);
		await faqs.Received(1).DeleteAsync("here");
	}

	[Fact]
	public async Task BannerDeleteMissing_NotFound()
	{
		var images = Substitute.For<IMenuBannerStorage>();
		var banners = Substitute.For<IMenuBannerRepository>();
		banners.GetAsync(Arg.Any<ContentReference>(), Arg.Any<CancellationToken>()).Returns((MenuBanner?)null);
		using var app = new TestApplication(services =>
		{
			services.AddSingleton(images);
			services.AddSingleton(banners);
		});
		var banner = new MenuBanner
		{
			Image = new ContentReference("banners/gone.png"), Url = new Uri("https://example.org"), StartsAt = null,
			EndsAt = null, CreatedAt = DateTimeOffset.UnixEpoch
		};

		var result = await (await OwnerAsync(app)).Banners.DeleteAsync(banner);

		Assert.Equal(ServerOutcome.NotFound, result);
		await images.DidNotReceiveWithAnyArgs().DeleteAsync(default);
	}

	[Fact]
	public async Task SeasonalDeleteMissing_NotFound()
	{
		var seasonals = Substitute.For<IMenuSeasonalsStorage>();
		seasonals.ListAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ContentReference>());
		using var app = new TestApplication(services => services.AddSingleton(seasonals));

		var result = await (await OwnerAsync(app)).Seasonals.DeleteAsync(new ContentReference("seasonals/gone.png"));

		Assert.Equal(ServerOutcome.NotFound, result);
		await seasonals.DidNotReceiveWithAnyArgs().DeleteAsync(default);
	}

	[Fact]
	public async Task IconDeleteWithNoIcon_NotFound()
	{
		using var app = new TestApplication();

		var result = await (await OwnerAsync(app)).Icon.DeleteAsync();

		Assert.Equal(ServerOutcome.NotFound, result);
	}
}
