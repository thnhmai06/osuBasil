using Basil.Application.Content.Persistence;
using Basil.Application.Content.Storage;
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
}