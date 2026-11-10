using Basil.Application.Chat.Objects;
using Basil.Application.Servers.Messages.Channels;
using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Infrastructure.Servers.Handlers.Channels;
using Basil.Testing;

namespace Basil.Infrastructure.Servers.Tests.Handlers;

/// <summary>The messages that create, configure and delete the general channels of the server.</summary>
public sealed class ChannelHandlerTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task CreateGeneralChannel_WithTheRight_CreatesTheChannel()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));

		var result = await new CreateGeneralChannelHandler(app.Server)
			.Handle(new CreateGeneralChannel(admin, Record("#new")), Token);

		Assert.True(result.IsOk);
		Assert.NotNull(app.Server.ScopeTo(admin).Channels.Find("#new").Channel);
	}

	[Fact]
	public async Task CreateGeneralChannel_WithoutTheRight_IsDeniedAndCreatesNothing()
	{
		using var app = new TestApplication();
		var plain = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));

		var result = await new CreateGeneralChannelHandler(app.Server)
			.Handle(new CreateGeneralChannel(plain, Record("#new")), Token);

		Assert.NotNull(result.Missing);
		Assert.Null(app.Server.ScopeTo(admin).Channels.Find("#new").Channel);
	}

	[Fact]
	public async Task ConfigureGeneralChannel_WithTheRight_ChangesTheTopicAndAnswersWithTheChannel()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		await new CreateGeneralChannelHandler(app.Server).Handle(new CreateGeneralChannel(admin, Record("#topic")), Token);

		var answer = await new ConfigureGeneralChannelHandler(app.Server)
			.Handle(new ConfigureGeneralChannel(admin, "#topic", new GeneralChannelChange(Topic: "hello")), Token);

		Assert.True(answer.Result.IsOk);
		Assert.Equal("hello", answer.Channel!.Topic);
	}

	[Fact]
	public async Task ConfigureGeneralChannel_OfAChannelThatDoesNotExist_AnswersWithoutAChannel()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));

		var answer = await new ConfigureGeneralChannelHandler(app.Server)
			.Handle(new ConfigureGeneralChannel(admin, "#missing", new GeneralChannelChange(Topic: "x")), Token);

		Assert.False(answer.Result.IsOk);
		Assert.Null(answer.Channel);
	}

	[Fact]
	public async Task DeleteGeneralChannel_WithTheRight_RemovesTheChannel()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));
		await new CreateGeneralChannelHandler(app.Server).Handle(new CreateGeneralChannel(admin, Record("#gone")), Token);

		var answer = await new DeleteGeneralChannelHandler(app.Server).Handle(new DeleteGeneralChannel(admin, "#gone"), Token);

		Assert.True(answer.Result.IsOk);
		Assert.Null(app.Server.ScopeTo(admin).Channels.Find("#gone").Channel);
	}

	[Fact]
	public async Task DeleteGeneralChannel_OfAChannelThatDoesNotExist_AnswersWithoutAChannel()
	{
		using var app = new TestApplication();
		var admin = await app.LoginAsync(await app.CreateUserAsync("Admin", "password", UserPermissionsGroups.All));

		var answer = await new DeleteGeneralChannelHandler(app.Server).Handle(new DeleteGeneralChannel(admin, "#missing"), Token);

		Assert.False(answer.Result.IsOk);
		Assert.Null(answer.Channel);
	}

	private static GeneralChannelRecord Record(string name)
	{
		return new GeneralChannelRecord { Name = name, Value = new GeneralChannelData() };
	}
}
