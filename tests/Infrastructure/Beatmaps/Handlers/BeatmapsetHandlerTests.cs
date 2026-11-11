using Basil.Application.Beatmaps.Messages;
using Basil.Application.Beatmaps.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;
using Basil.Infrastructure.Beatmaps.Handlers;
using Basil.Testing;
using static Basil.Testing.BeatmapsetWorld;

namespace Basil.Infrastructure.Beatmaps.Tests.Handlers;

/// <summary>The messages that delete or change a beatmapset with the privileges of the sending connection.</summary>
public sealed class BeatmapsetHandlerTests
{
	private static readonly UserPrivileges Manager = UserPrivilegesExtensions.Default | UserPrivileges.Tournament;

	private static async Task<(TestApplication App, BeatmapsetWorld World, BeatmapsetRecord Set, Connection Actor)>
		SetupAsync(UserPrivileges privileges)
	{
		var world = new BeatmapsetWorld();
		var app = world.CreateApplication();
		var imported = await app.Beatmapsets.ImportAsync(
			new MemoryStream(Osz.Build(null, new Diff("Easy", "easy file"))), TestContext.Current.CancellationToken);
		var actor = await app.LoginAsync(await app.CreateUserAsync("Manager", "password", privileges));
		return (app, world, imported.Set!, actor);
	}

	[Fact]
	public async Task ByAManager_RemovesTheSet()
	{
		var (app, world, set, actor) = await SetupAsync(Manager);
		using var _ = app;

		var result = await new DeleteBeatmapsetHandler(app.Beatmapsets).Handle(new DeleteBeatmapset(actor, set.Id),
			TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task NoManage_DeniedWithPrivilege()
	{
		var (app, world, set, actor) = await SetupAsync(UserPrivilegesExtensions.Default);
		using var _ = app;

		var result = await new DeleteBeatmapsetHandler(app.Beatmapsets).Handle(new DeleteBeatmapset(actor, set.Id),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserPrivileges.Tournament, result.Missing);
		Assert.Single(world.Sets.Records);
	}

	[Fact]
	public async Task ByManager_AppliesEveryField()
	{
		var (app, _, set, actor) = await SetupAsync(Manager);
		using var _ = app;

		var result = await new PatchBeatmapsetHandler(app.Beatmapsets).Handle(
			new PatchBeatmapset(actor, set.Id, BeatmapsetStatus.Loved, false, true),
			TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Equal(BeatmapsetStatus.Loved, set.Status);
		Assert.False(set.Visible);
		Assert.True(set.Locked);
	}

	[Fact]
	public async Task NoField_OkNoChange()
	{
		var (app, _, set, actor) = await SetupAsync(UserPrivilegesExtensions.Default);
		using var _ = app;

		var result = await new PatchBeatmapsetHandler(app.Beatmapsets).Handle(
			new PatchBeatmapset(actor, set.Id, null, null, null), TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.Ok, result.Outcome);
		Assert.Equal(BeatmapsetStatus.Ranked, set.Status);
	}

	[Fact]
	public async Task PatchNoManage_DeniedNoChange()
	{
		var (app, _, set, actor) = await SetupAsync(UserPrivilegesExtensions.Default);
		using var _ = app;

		var result = await new PatchBeatmapsetHandler(app.Beatmapsets).Handle(
			new PatchBeatmapset(actor, set.Id, BeatmapsetStatus.Loved, false, null),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserPrivileges.Tournament, result.Missing);
		Assert.Equal(BeatmapsetStatus.Ranked, set.Status);
		Assert.True(set.Visible);
	}

	[Fact]
	public async Task UnknownSetManager_NotFound()
	{
		var (app, _, _, actor) = await SetupAsync(Manager);
		using var _ = app;

		var result = await new DeleteBeatmapsetHandler(app.Beatmapsets).Handle(
			new DeleteBeatmapset(actor, int.MaxValue), TestContext.Current.CancellationToken);

		Assert.Equal(BeatmapsetOutcome.NotFound, result.Outcome);
	}

	[Fact]
	public async Task NoPrivilegeUnknown_DeniedLikeKnown()
	{
		var (app, _, set, actor) = await SetupAsync(UserPrivilegesExtensions.Default);
		using var _ = app;
		var handler = new DeleteBeatmapsetHandler(app.Beatmapsets);

		var known = await handler.Handle(new DeleteBeatmapset(actor, set.Id), TestContext.Current.CancellationToken);
		var unknown = await handler.Handle(new DeleteBeatmapset(actor, int.MaxValue),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserPrivileges.Tournament, unknown.Missing);
		Assert.Equal(known.Missing, unknown.Missing);
	}
}