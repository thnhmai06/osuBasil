using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Objects.Events;
using Basil.Application.Servers.Objects;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Servers.Tests.Objects;

/// <summary>Behaviour of BasilBot speaking in a channel, from the XML contract of <c>Server.SayAsync</c>.</summary>
public sealed class ServerSayTests
{
	private const string ChannelName = "#announce";

	private static async Task<GeneralChannel> OpenChannelAsync(TestApplication app)
	{
		var admin = await app.CreateUserAsync("ChannelAdmin", "password", UserPrivilegesExtensions.All);
		var actor = await app.LoginAsync(admin);
		var record = new GeneralChannelRecord { Name = ChannelName, Value = new GeneralChannelData() };
		var created = await app.Server.ScopeTo(actor).Channels
			.CreateAsync(record);
		Assert.True(created == ServerOutcome.Ok);
		return app.Channels.Find(ChannelName)!;
	}

	[Fact]
	public async Task Text_PostsAsBot()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var channel = await OpenChannelAsync(app);

		var said = await app.Server.SayAsync(channel, "Hello",
			ct: TestContext.Current.CancellationToken);

		Assert.True(said);
		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events.Last());
		Assert.Equal("Hello", posted.Message.Content);
		Assert.True(posted.Message.Author.IsBasilBot());
	}

	[Fact]
	public async Task WhitespaceText_PostedAsIs()
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var channel = await OpenChannelAsync(app);

		var said = await app.Server.SayAsync(channel, "  ", ct: TestContext.Current.CancellationToken);

		Assert.True(said);
		var posted = Assert.IsType<ChannelMessagePosted>(app.ChannelEvents.Events.Last());
		Assert.Equal("  ", posted.Message.Content);
	}

	[Theory]
	[InlineData("")]
	[InlineData("bad\0text")]
	public async Task EmptyOrInvalid_PostsNothing(string text)
	{
		using var app = new TestApplication();
		await app.SeedBotAsync();
		var channel = await OpenChannelAsync(app);
		var before = app.ChannelEvents.Events.Count;

		var said = await app.Server.SayAsync(channel, text, ct: TestContext.Current.CancellationToken);

		Assert.False(said);
		Assert.Equal(before, app.ChannelEvents.Events.Count);
	}
}