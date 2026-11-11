using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Events;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Users.Tests.Capabilities;

public sealed class ScopedUserSilenceTests
{
	private static readonly UserPrivileges Staff = UserPrivilegesExtensions.All;

	[Fact]
	public async Task Allowed_SilencesPublishesRecords()
	{
		using var app = new TestApplication();
		var actorRecord = await app.CreateUserAsync("Moderator", "password", Staff);
		var target = await app.CreateUserAsync("Silenced", "password");
		var actor = await app.LoginAsync(actorRecord);
		var end = app.Clock.GetUtcNow().AddHours(1);

		var result = await app.Users.Of(target).ScopeTo(actor).Silence.ImposeAsync(end, "test reason");

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Equal(end, target.Value.SilencedUntil);
		var silenced = Assert.IsType<UserSilenced>(app.UserEvents.Events[^1]);
		Assert.Equal(target, silenced.User);
		Assert.Equal(end, silenced.Until);
		var entry = Assert.Single(app.ModerationEntries);
		Assert.Equal(ModerationAction.Silence, entry.Action);
		Assert.Equal(actorRecord, entry.Actor);
		Assert.Equal(target, entry.Target);
		Assert.Equal("test reason", entry.Reason);
	}

	[Fact]
	public async Task EndNotFuture_InvalidSilenceNobody()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Moderator", "password", Staff));
		var target = await app.CreateUserAsync("Target", "password");

		var result = await app.Users.Of(target).ScopeTo(actor).Silence.ImposeAsync(app.Clock.GetUtcNow());

		Assert.Equal(UserOutcome.InvalidSilence, result.Outcome);
		Assert.Null(target.Value.SilencedUntil);
	}

	[Fact]
	public async Task EqualPrivileges_ReturnsNotOutranking()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("EqualActor", "password", Staff));
		var target = await app.CreateUserAsync("EqualTarget", "password", Staff);

		var result = await app.Users.Of(target).ScopeTo(actor).Silence.ImposeAsync(
			app.Clock.GetUtcNow().AddHours(1));

		Assert.Equal(UserOutcome.NotOutranking, result.Outcome);
		Assert.Null(target.Value.SilencedUntil);
	}

	[Fact]
	public async Task TargetIsActor_ReturnsNotOutranking()
	{
		using var app = new TestApplication();
		var record = await app.CreateUserAsync("SelfSilence", "password", Staff);
		var actor = await app.LoginAsync(record);

		var result = await app.Users.Of(record).ScopeTo(actor).Silence.ImposeAsync(
			app.Clock.GetUtcNow().AddHours(1));

		Assert.Equal(UserOutcome.NotOutranking, result.Outcome);
	}

	[Fact]
	public async Task NoModerator_NamesMissing()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var target = await app.CreateUserAsync("Target", "password");

		var result = await app.Users.Of(target).ScopeTo(actor).Silence.ImposeAsync(
			app.Clock.GetUtcNow().AddHours(1));

		Assert.Equal(UserPrivileges.Moderator, result.Missing);
		Assert.Null(target.Value.SilencedUntil);
	}

	[Fact]
	public async Task SilencedAccount_EndsPublishesRecords()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("LiftMod", "password", Staff));
		var target = await app.CreateUserAsync("Lifted", "password");
		var scoped = app.Users.Of(target).ScopeTo(actor);
		Assert.True((await scoped.Silence.ImposeAsync(app.Clock.GetUtcNow().AddHours(1))).IsOk);

		var result = await scoped.Silence.LiftAsync();

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Null(target.Value.SilencedUntil);
		Assert.Equal(target, Assert.IsType<UserUnsilenced>(app.UserEvents.Events[^1]).User);
		Assert.Equal(ModerationAction.Unsilence, app.ModerationEntries[^1].Action);
	}

	[Fact]
	public async Task NotSilenced_OkRecordsNothing()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("LiftMod", "password", Staff));
		var target = await app.CreateUserAsync("Free", "password");

		var result = await app.Users.Of(target).ScopeTo(actor).Silence.LiftAsync(
			);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Empty(app.ModerationEntries);
	}

	[Fact]
	public async Task NoModeratorLift_KeepsSilence()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Plain", "password"));
		var target = await app.CreateUserAsync("Silenced", "password");
		app.Silence(target, app.Clock.GetUtcNow().AddHours(1));

		var result = await app.Users.Of(target).ScopeTo(actor).Silence.LiftAsync(
			);

		Assert.Equal(UserPrivileges.Moderator, result.Missing);
		Assert.NotNull(target.Value.SilencedUntil);
	}

	[Fact]
	public async Task AfterEndTime_NotSilenced()
	{
		using var app = new TestApplication();
		var target = await app.CreateUserAsync("Expiring", "password");
		app.Silence(target, app.Clock.GetUtcNow().AddMinutes(1));
		Assert.True(target.Value.IsSilenced(app.Clock.GetUtcNow()));

		app.Clock.Advance(TimeSpan.FromMinutes(1));

		Assert.False(target.Value.IsSilenced(app.Clock.GetUtcNow()));
	}
}
