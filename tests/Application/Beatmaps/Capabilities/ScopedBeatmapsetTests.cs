using Basil.Application.Beatmaps.Objects;
using Basil.Application.Beatmaps.Objects.Events;
using Basil.Application.Common;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;
using Basil.Testing;
using static Basil.Testing.BeatmapsetWorld;

namespace Basil.Application.Beatmaps.Tests.Capabilities;

/// <summary>
///     Managing a stored beatmapset as a connection: deleting, hiding and locking need
///     <see cref="UserPrivileges.Tournament" />, and a locked set cannot be deleted.
/// </summary>
public sealed class ScopedBeatmapsetTests
{
	private static readonly UserPrivileges Manager = UserPrivilegesExtensions.Default | UserPrivileges.Tournament;

	private static async Task<(TestApplication App, BeatmapsetWorld World, Beatmapset Set, Connection Actor)>
		SetupAsync(
			UserPrivileges? privileges = null, Action<TestApplication, UserRecord>? restrict = null)
	{
		var world = new BeatmapsetWorld();
		var app = world.CreateApplication();
		var imported = await app.Beatmapsets.ImportAsync(
			new MemoryStream(Osz.Build(null, new Diff("Easy", "easy file"))), TestContext.Current.CancellationToken);
		var user = await app.CreateUserAsync("Manager", "password", privileges ?? Manager);
		restrict?.Invoke(app, user);
		var actor = await app.LoginAsync(user);
		return (app, world, app.Beatmapsets.Of(imported.Set!), actor);
	}

	[Fact]
	public async Task
		WithManage_DeletesSetFiles()
	{
		var (app, world, set, actor) = await SetupAsync();
		using var _ = app;

		var result = await set.ScopeTo(actor).DeleteAsync();

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Empty(world.Sets.Records);
		Assert.Empty(world.Beatmaps.Records);
		Assert.Empty(world.Storage.Stored);
		Assert.Equal(set.Record, Assert.IsType<BeatmapsetDeleted>(app.BeatmapsetEvents.Events[^1]).Set);
	}

	[Fact]
	public async Task ActorNotConnection_UsesEffectiveRights()
	{
		var (app, world, set, actor) = await SetupAsync();
		using var _ = app;

		var allowed = await set.ScopeTo(new FixedActor(actor.Record, Manager)).DeleteAsync();

		Assert.Equal(BeatmapsetOutcome.Ok, allowed.Outcome);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task ActorWithoutManage_Denied()
	{
		var (app, world, set, actor) = await SetupAsync();
		using var _ = app;

		var denied = await set.ScopeTo(new FixedActor(actor.Record, UserPrivilegesExtensions.Default)).DeleteAsync();

		Assert.Equal(UserPrivileges.Tournament, denied.Missing);
		Assert.NotEmpty(world.Sets.Records);
	}

	/// <summary>An actor with privileges fixed in advance, as a caller other than a connection would be.</summary>
	[Fact]
	public async Task NewSet_IsRanked()
	{
		var (app, _, set, _) = await SetupAsync();
		using var _ = app;

		Assert.Equal(BeatmapsetStatus.Ranked, set.Record.Status);
	}

	[Fact]
	public async Task StatusWithManage_ChangedStored()
	{
		var (app, world, set, actor) = await SetupAsync();
		using var _ = app;

		var result = await set.ScopeTo(actor).SetStatusAsync(BeatmapsetStatus.Loved);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Equal(BeatmapsetStatus.Loved, set.Record.Status);
		Assert.Equal(BeatmapsetStatus.Loved, world.Sets.Records[set.Record.Id].Status);
	}

	[Fact]
	public async Task StatusWithoutManage_Denied()
	{
		var (app, _, set, actor) = await SetupAsync();
		using var _ = app;

		var denied = await set.ScopeTo(new FixedActor(actor.Record, UserPrivilegesExtensions.Default)).SetStatusAsync(
			BeatmapsetStatus.Loved);

		Assert.Equal(UserPrivileges.Tournament, denied.Missing);
		Assert.Equal(BeatmapsetStatus.Ranked, set.Record.Status);
	}

	[Fact]
	public async Task LockedSetDelete_RefusedLocked()
	{
		var (app, world, set, actor) = await SetupAsync();
		using var _ = app;
		set.Record.Locked = true;
		var events = app.BeatmapsetEvents.Events.Count;

		var result = await set.ScopeTo(actor).DeleteAsync();

		Assert.Equal(BeatmapsetOutcome.Locked, result.Outcome);
		Assert.Single(world.Sets.Records);
		Assert.Single(world.Storage.Stored);
		Assert.Equal(events, app.BeatmapsetEvents.Events.Count);
	}

	[Fact]
	public async Task DeleteWithoutManage_DeniedPermission()
	{
		var (app, world, set, actor) = await SetupAsync(UserPrivilegesExtensions.Default);
		using var _ = app;

		var result = await set.ScopeTo(actor).DeleteAsync();

		Assert.False(result.IsOk);
		Assert.Equal(UserPrivileges.Tournament, result.Missing);
		Assert.Single(world.Sets.Records);
		Assert.Single(world.Storage.Stored);
	}

	[Fact]
	public async Task VisibleFalse_SetHidden()
	{
		var (app, _, set, actor) = await SetupAsync();
		using var _ = app;

		var result = await set.ScopeTo(actor).SetVisibleAsync(false);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.False(set.Record.Visible);
	}

	[Fact]
	public async Task VisibleStoreFails_ReachesCaller()
	{
		var (app, world, set, actor) = await SetupAsync();
		using var _ = app;
		world.Sets.UpdateFailure = new InvalidOperationException("store down");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			set.ScopeTo(actor).SetVisibleAsync(false));
	}

	[Fact]
	public async Task LockedStoreFails_ReachesCaller()
	{
		var (app, world, set, actor) = await SetupAsync();
		using var _ = app;
		world.Sets.UpdateFailure = new InvalidOperationException("store down");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			set.ScopeTo(actor).SetLockedAsync(true));
	}

	[Fact]
	public async Task StatusStoreFails_ReachesCaller()
	{
		var (app, world, set, actor) = await SetupAsync();
		using var _ = app;
		world.Sets.UpdateFailure = new InvalidOperationException("store down");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			set.ScopeTo(actor).SetStatusAsync(BeatmapsetStatus.Loved));
	}

	[Fact]
	public async Task VisibleTrue_SetShown()
	{
		var (app, _, set, actor) = await SetupAsync();
		using var _ = app;
		await set.ScopeTo(actor).SetVisibleAsync(false);

		var result = await set.ScopeTo(actor).SetVisibleAsync(true);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.True(set.Record.Visible);
	}

	[Fact]
	public async Task VisibleWithoutManage_Denied()
	{
		var (app, _, set, actor) = await SetupAsync(UserPrivilegesExtensions.Default);
		using var _ = app;

		var result = await set.ScopeTo(actor).SetVisibleAsync(false);

		Assert.Equal(UserPrivileges.Tournament, result.Missing);
		Assert.True(set.Record.Visible);
	}

	[Fact]
	public async Task LockedTrue_SetLocked()
	{
		var (app, _, set, actor) = await SetupAsync();
		using var _ = app;

		var result = await set.ScopeTo(actor).SetLockedAsync(true);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.True(set.Record.Locked);
	}

	[Fact]
	public async Task LockedFalse_SetDeletable()
	{
		var (app, world, set, actor) = await SetupAsync();
		using var _ = app;
		await set.ScopeTo(actor).SetLockedAsync(true);

		await set.ScopeTo(actor).SetLockedAsync(false);
		var result = await set.ScopeTo(actor).DeleteAsync();

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task LockedWithoutManage_Denied()
	{
		var (app, _, set, actor) = await SetupAsync(UserPrivilegesExtensions.Default);
		using var _ = app;

		var result = await set.ScopeTo(actor).SetLockedAsync(true);

		Assert.Equal(UserPrivileges.Tournament, result.Missing);
		Assert.False(set.Record.Locked);
	}

	[Fact]
	public async Task Of_ReturnsStoredSet()
	{
		var (app, world, set, _) = await SetupAsync();
		using var _ = app;

		Assert.Equal(world.Sets.Records.Values.Single(), set.Record);
	}

	private sealed class FixedActor(UserRecord record, UserPrivileges privileges) : IActor
	{
		public UserRecord Record { get; } = record;

		public UserPrivileges EffectivePrivileges(DateTimeOffset now)
		{
			return privileges;
		}
	}
}