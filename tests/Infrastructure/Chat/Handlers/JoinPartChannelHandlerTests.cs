using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Servers.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Infrastructure.Chat.Handlers;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Infrastructure.Chat.Tests.Handlers;

/// <summary>Joining and leaving a channel through messages. Outcomes come from the channel's own contract.</summary>
public sealed class JoinPartChannelHandlerTests
{
	private const string ChannelName = "#lobby";

	private static async Task<Connection> OpenChannelAsync(TestApplication app)
	{
		var admin = await app.CreateUserAsync("ChannelAdmin", "password", UserPrivilegesExtensions.All);
		var actor = await app.LoginAsync(admin);
		var created = await app.Server.ScopeTo(actor).Channels.CreateAsync(
			new GeneralChannelRecord { Name = ChannelName, Value = new GeneralChannelData() });
		Assert.True(created == ServerOutcome.Ok);
		return actor;
	}

	private static JoinChannelHandler Joiner(TestApplication app)
	{
		return new JoinChannelHandler(app.Provider.GetRequiredService<ChannelDirectory>());
	}

	private static PartChannelHandler Parter(TestApplication app)
	{
		return new PartChannelHandler(app.Provider.GetRequiredService<ChannelDirectory>());
	}

	[Fact]
	public async Task OpenGeneralChannel_AddsTheSender()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);

		var result = await Joiner(app).Handle(new JoinChannel(actor, new ChatTarget.General(ChannelName)),
			TestContext.Current.CancellationToken);

		Assert.True(result == ChannelOutcome.Ok);
		Assert.Contains(actor, app.Channels.Find(ChannelName)!.Members);
	}

	[Fact]
	public async Task JoinMissing_AnswersNotFound()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);

		var result = await Joiner(app).Handle(new JoinChannel(actor, new ChatTarget.General("#nowhere")),
			TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.NotFound, result);
	}

	[Fact]
	public async Task SenderIn_AnswersAlreadyMember()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);
		await Joiner(app).Handle(new JoinChannel(actor, new ChatTarget.General(ChannelName)),
			TestContext.Current.CancellationToken);

		var result = await Joiner(app).Handle(new JoinChannel(actor, new ChatTarget.General(ChannelName)),
			TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.AlreadyMember, result);
	}

	[Fact]
	public async Task SenderIn_RemovesSender()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);
		await Joiner(app).Handle(new JoinChannel(actor, new ChatTarget.General(ChannelName)),
			TestContext.Current.CancellationToken);

		var result = await Parter(app).Handle(new PartChannel(actor, new ChatTarget.General(ChannelName)),
			TestContext.Current.CancellationToken);

		Assert.True(result == ChannelOutcome.Ok);
		Assert.DoesNotContain(actor, app.Channels.Find(ChannelName)!.Members);
	}

	[Fact]
	public async Task SenderOut_AnswersNotMember()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);

		var result = await Parter(app).Handle(new PartChannel(actor, new ChatTarget.General(ChannelName)),
			TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.NotMember, result);
	}

	[Fact]
	public async Task PartMissing_AnswersNotFound()
	{
		using var app = new TestApplication();
		var actor = await OpenChannelAsync(app);

		var result = await Parter(app).Handle(new PartChannel(actor, new ChatTarget.General("#nowhere")),
			TestContext.Current.CancellationToken);

		Assert.Equal(ChannelOutcome.NotFound, result);
	}
}