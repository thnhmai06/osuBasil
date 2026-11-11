using Basil.Application.Chat.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Basil.Application.Servers.Tests.Capabilities;

/// <summary>Creating a general channel needs the privilege to manage channels, and a name is used once.</summary>
public sealed class ScopedServerChannelsTests
{
	private static GeneralChannelRecord Record(string name)
	{
		return new GeneralChannelRecord { Name = name, Value = new GeneralChannelData() };
	}

	[Fact]
	public async Task NoManageChannels_Denied()
	{
		using var app = new TestApplication();
		var player = await RoomFixture.PlayerAsync(app, "Plain");

		var result = await app.Server.ScopeTo(player).Channels.CreateAsync(Record("#denied"));

		Assert.Equal(UserPrivileges.Moderator, result.Missing);
		Assert.Null(app.Channels.Find("#denied"));
	}

	[Fact]
	public async Task ManageChannels_OpenNameTaken()
	{
		using var app = new TestApplication();
		var manager = await RoomFixture.PlayerAsync(app, "Boss",
			UserPrivilegesExtensions.Default | UserPrivileges.Moderator);

		var first = await app.Server.ScopeTo(manager).Channels
			.CreateAsync(Record("#fresh"));
		var second = await app.Server.ScopeTo(manager).Channels
			.CreateAsync(Record("#FRESH"));

		Assert.Equal(ServerOutcome.Ok, first.Outcome);
		Assert.Equal(ServerOutcome.NameTaken, second.Outcome);
		Assert.NotNull(app.Channels.Find("#fresh"));
	}

	[Fact]
	public async Task StoreFailure_NoChannelRethrows()
	{
		var repository = Substitute.For<IChannelRepository>();
		repository.CreateOrUpdateAsync(Arg.Any<GeneralChannelRecord>())
			.Returns(Task.FromException(new IOException("disk full")));
		using var app = new TestApplication(services => services.AddSingleton(repository));
		var manager = await RoomFixture.PlayerAsync(app, "Boss",
			UserPrivilegesExtensions.Default | UserPrivileges.Moderator);

		await Assert.ThrowsAsync<IOException>(() => app.Server.ScopeTo(manager).Channels
			.CreateAsync(Record("#broken")));

		Assert.Null(app.Channels.Find("#broken"));
	}
}