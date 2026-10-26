using Basil.Application.Runtime.Chat;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Runtime.Tests.Servers;

/// <summary>Behaviour of BasilBot speaking in a channel, from the XML contract of <c>Server.SayAsync</c>.</summary>
public sealed class ServerSayTests
{
	private const string ChannelName = "#announce";

	private static async Task<GeneralChannel> OpenChannelAsync(TestApplication app)
	{
		var admin = await app.CreateUserAsync("ChannelAdmin", "password", UserPermissionsGroups.All);
		var by = await app.LoginAsync(admin);
		var record = new GeneralChannelRecord { Name = ChannelName, Value = new GeneralChannelData() };
		var created = await app.Server.ScopeTo(by).Channels.CreateAsync(record, TestContext.Current.CancellationToken);
		Assert.True(created.IsOk);
		return app.Channels.Find(ChannelName)!;
	}

	[Fact]
	public async Task SayAsync_Text_PostsItAsTheBot()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var channel = await OpenChannelAsync(app);

		var said = await app.Server.SayAsync(channel, "Hello", cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(said);
		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events.Last());
		Assert.Equal("Hello", posted.Message.Content);
		Assert.True(posted.Message.Author.IsBasilBot());
	}

	[Fact]
	public async Task SayAsync_WhitespaceOnlyText_IsPostedAsIs()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var channel = await OpenChannelAsync(app);

		var said = await app.Server.SayAsync(channel, "  ", cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(said);
		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events.Last());
		Assert.Equal("  ", posted.Message.Content);
	}

	[Theory]
	[InlineData("")]
	[InlineData("bad\0text")]
	public async Task SayAsync_EmptyOrInvalidText_PostsNothing(string text)
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var channel = await OpenChannelAsync(app);
		var before = app.ChannelEvents.Events.Count;

		var said = await app.Server.SayAsync(channel, text, cancellationToken: TestContext.Current.CancellationToken);

		Assert.False(said);
		Assert.Equal(before, app.ChannelEvents.Events.Count);
	}
}
