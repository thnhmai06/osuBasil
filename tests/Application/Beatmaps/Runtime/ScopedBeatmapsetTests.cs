using Basil.Application.Beatmaps.Runtime;
using Basil.Application.Common;
using Basil.Application.Users.Connections;
using Basil.Domain.Users;
using Basil.Testing;
using static Basil.Testing.BeatmapsetWorld;

namespace Basil.Application.Beatmaps.Tests.Runtime;

/// <summary>
///     Managing a stored beatmapset as a connection: deleting, hiding and locking need
///     <see cref="UserPermissions.BeatmapsetManage" />, and a locked set cannot be deleted.
/// </summary>
public sealed class ScopedBeatmapsetTests
{
	private static readonly UserPermissions Manager = UserPermissionsGroups.Default | UserPermissions.BeatmapsetManage;

	private static async Task<(TestApplication App, BeatmapsetWorld World, Beatmapset Set, Connection By)> SetupAsync(
		UserPermissions? permissions = null, Action<TestApplication, UserRecord>? restrict = null)
	{
		var world = new BeatmapsetWorld();
		var app = world.CreateApplication();
		var imported = await app.Beatmapsets.ImportAsync(
			new MemoryStream(Osz.Build(null, new Diff("Easy", "easy file"))), TestContext.Current.CancellationToken);
		var user = await app.CreateUserAsync("Manager", "password", permissions ?? Manager);
		restrict?.Invoke(app, user);
		var by = await app.LoginAsync(user);
		return (app, world, app.Beatmapsets.Of(imported.Set!), by);
	}

	[Fact]
	public async Task
		DeleteAsync_WithBeatmapsetManage_DeletesTheSetItsBeatmapsAndItsFilesAndPublishesBeatmapsetDeleted()
	{
		var (app, world, set, by) = await SetupAsync();
		using var _ = app;

		var result = await set.ScopeTo(by).DeleteAsync(TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Empty(world.Sets.Records);
		Assert.Empty(world.Beatmaps.Records);
		Assert.Empty(world.Storage.Stored);
		Assert.Equal(set.Record, Assert.IsType<BeatmapsetDeleted>(app.BeatmapsetEvents.Events[^1]).Set);
	}

	[Fact]
	public async Task DeleteAsync_ByAnActorThatIsNoConnection_FollowsItsEffectivePermissions()
	{
		var (app, world, set, by) = await SetupAsync();
		using var _ = app;

		var allowed = await set.ScopeTo(new FixedActor(by.Record, Manager)).DeleteAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, allowed.Outcome);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task DeleteAsync_ByAnActorWithoutBeatmapsetManage_IsDeniedAndChangesNothing()
	{
		var (app, world, set, by) = await SetupAsync();
		using var _ = app;

		var denied = await set.ScopeTo(new FixedActor(by.Record, UserPermissionsGroups.Default)).DeleteAsync(
			TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.BeatmapsetManage, denied.Missing);
		Assert.NotEmpty(world.Sets.Records);
	}

	/// <summary>An actor with permissions fixed in advance, as a caller other than a connection would be.</summary>
	private sealed class FixedActor(UserRecord record, UserPermissions permissions) : IActor
	{
		public UserRecord Record { get; } = record;

		public UserPermissions EffectivePermissions(DateTimeOffset now)
		{
			return permissions;
		}
	}

	[Fact]
	public async Task DeleteAsync_OfALockedSet_IsRefusedAsLockedAndChangesNothing()
	{
		var (app, world, set, by) = await SetupAsync();
		using var _ = app;
		set.Record.Locked = true;
		var events = app.BeatmapsetEvents.Events.Count;

		var result = await set.ScopeTo(by).DeleteAsync(TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Locked, result.Outcome);
		Assert.Single(world.Sets.Records);
		Assert.Single(world.Storage.Stored);
		Assert.Equal(events, app.BeatmapsetEvents.Events.Count);
	}

	[Fact]
	public async Task DeleteAsync_WithoutBeatmapsetManage_IsDeniedWithExactlyThatPermission()
	{
		var (app, world, set, by) = await SetupAsync(UserPermissionsGroups.Default);
		using var _ = app;

		var result = await set.ScopeTo(by).DeleteAsync(TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.BeatmapsetManage, result.Missing);
		Assert.Single(world.Sets.Records);
		Assert.Single(world.Storage.Stored);
	}

	[Fact]
	public async Task DeleteAsync_WithBeatmapsetManageSuspended_IsDenied()
	{
		var (app, world, set, by) =
			await SetupAsync(restrict: (app, user) => app.AddRestriction(user, UserPermissions.BeatmapsetManage));
		using var _ = app;

		var result = await set.ScopeTo(by).DeleteAsync(TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.BeatmapsetManage, result.Missing);
		Assert.Single(world.Sets.Records);
	}

	[Fact]
	public async Task SetVisibleAsync_ToFalse_HidesTheSet()
	{
		var (app, _, set, by) = await SetupAsync();
		using var _ = app;

		var result = await set.ScopeTo(by).SetVisibleAsync(false, TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.False(set.Record.Visible);
	}

	[Fact]
	public async Task SetVisibleAsync_ToTrueAgain_ShowsTheSet()
	{
		var (app, _, set, by) = await SetupAsync();
		using var _ = app;
		await set.ScopeTo(by).SetVisibleAsync(false, TestContext.Current.CancellationToken);

		var result = await set.ScopeTo(by).SetVisibleAsync(true, TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.True(set.Record.Visible);
	}

	[Fact]
	public async Task SetVisibleAsync_WithoutBeatmapsetManage_IsDeniedAndChangesNothing()
	{
		var (app, _, set, by) = await SetupAsync(UserPermissionsGroups.Default);
		using var _ = app;

		var result = await set.ScopeTo(by).SetVisibleAsync(false, TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.BeatmapsetManage, result.Missing);
		Assert.True(set.Record.Visible);
	}

	[Fact]
	public async Task SetLockedAsync_ToTrue_LocksTheSet()
	{
		var (app, _, set, by) = await SetupAsync();
		using var _ = app;

		var result = await set.ScopeTo(by).SetLockedAsync(true, TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.True(set.Record.Locked);
	}

	[Fact]
	public async Task SetLockedAsync_ToFalse_UnlocksTheSetSoItCanBeDeleted()
	{
		var (app, world, set, by) = await SetupAsync();
		using var _ = app;
		await set.ScopeTo(by).SetLockedAsync(true, TestContext.Current.CancellationToken);

		await set.ScopeTo(by).SetLockedAsync(false, TestContext.Current.CancellationToken);
		var result = await set.ScopeTo(by).DeleteAsync(TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task SetLockedAsync_WithoutBeatmapsetManage_IsDeniedAndChangesNothing()
	{
		var (app, _, set, by) = await SetupAsync(UserPermissionsGroups.Default);
		using var _ = app;

		var result = await set.ScopeTo(by).SetLockedAsync(true, TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.BeatmapsetManage, result.Missing);
		Assert.False(set.Record.Locked);
	}

	[Fact]
	public async Task Of_GivesTheObjectOfTheStoredSet()
	{
		var (app, world, set, _) = await SetupAsync();
		using var _ = app;

		Assert.Equal(world.Sets.Records.Values.Single(), set.Record);
	}
}