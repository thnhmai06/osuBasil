using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;
using Basil.Infrastructure.Beatmaps.Handlers;
using Basil.Testing;
using static Basil.Testing.BeatmapsetWorld;

namespace Basil.Infrastructure.Beatmaps.Tests.Handlers;

/// <summary>The messages that delete or change a beatmapset with the rights of the sending connection.</summary>
public sealed class BeatmapsetHandlerTests
{
	private static readonly UserPermissions Manager = UserPermissionsGroups.Default | UserPermissions.BeatmapsetManage;

	private static async Task<(TestApplication App, BeatmapsetWorld World, BeatmapsetRecord Set, Connection Actor)>
		SetupAsync(UserPermissions permissions)
	{
		var world = new BeatmapsetWorld();
		var app = world.CreateApplication();
		var imported = await app.Beatmapsets.ImportAsync(
			new MemoryStream(Osz.Build(null, new Diff("Easy", "easy file"))), TestContext.Current.CancellationToken);
		var actor = await app.LoginAsync(await app.CreateUserAsync("Manager", "password", permissions));
		return (app, world, imported.Set!, actor);
	}

	[Fact]
	public async Task Delete_ByAManager_RemovesTheSet()
	{
		var (app, world, set, actor) = await SetupAsync(Manager);
		using var _ = app;

		var result = await new DeleteBeatmapsetHandler(app.Beatmapsets).Handle(new DeleteBeatmapset(actor, set),
			TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task Delete_WithoutBeatmapsetManage_IsDeniedWithThatPermission()
	{
		var (app, world, set, actor) = await SetupAsync(UserPermissionsGroups.Default);
		using var _ = app;

		var result = await new DeleteBeatmapsetHandler(app.Beatmapsets).Handle(new DeleteBeatmapset(actor, set),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.BeatmapsetManage, result.Missing);
		Assert.Single(world.Sets.Records);
	}

	[Fact]
	public async Task Patch_ByAManager_AppliesEveryGivenField()
	{
		var (app, _, set, actor) = await SetupAsync(Manager);
		using var _ = app;

		var result = await new PatchBeatmapsetHandler(app.Beatmapsets).Handle(
			new PatchBeatmapset(actor, set, BeatmapsetStatus.Loved, false, true),
			TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Equal(BeatmapsetStatus.Loved, set.Status);
		Assert.False(set.Visible);
		Assert.True(set.Locked);
	}

	[Fact]
	public async Task Patch_WithNoField_IsOkAndChangesNothing()
	{
		var (app, _, set, actor) = await SetupAsync(UserPermissionsGroups.Default);
		using var _ = app;

		var result = await new PatchBeatmapsetHandler(app.Beatmapsets).Handle(
			new PatchBeatmapset(actor, set, null, null, null), TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Equal(BeatmapsetStatus.Ranked, set.Status);
	}

	[Fact]
	public async Task Patch_WithoutBeatmapsetManage_IsDeniedAndChangesNothing()
	{
		var (app, _, set, actor) = await SetupAsync(UserPermissionsGroups.Default);
		using var _ = app;

		var result = await new PatchBeatmapsetHandler(app.Beatmapsets).Handle(
			new PatchBeatmapset(actor, set, BeatmapsetStatus.Loved, false, null),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.BeatmapsetManage, result.Missing);
		Assert.Equal(BeatmapsetStatus.Ranked, set.Status);
		Assert.True(set.Visible);
	}
}
