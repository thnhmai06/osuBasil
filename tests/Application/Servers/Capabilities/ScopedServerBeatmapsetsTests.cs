using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Capabilities;
using Basil.Application.Servers.Objects;
using Basil.Domain.Users;
using Basil.Testing;
using static Basil.Testing.BeatmapsetWorld;

namespace Basil.Application.Servers.Tests.Capabilities;

/// <summary>
///     Uploading a beatmapset as a connection, from the XML contract of <see cref="ScopedServerBeatmapsets" />: it needs
///     <see cref="UserPermissions.BeatmapsetUpload" />, a locked area refuses everyone but managers, and a set the
///     server has is never overwritten.
/// </summary>
public sealed class ScopedServerBeatmapsetsTests
{
	private static readonly Diff Easy = new("Easy", "easy file");

	private static async Task<(TestApplication App, BeatmapsetWorld World, ScopedServerBeatmapsets Uploads)> SetupAsync(
		UserPermissions permissions, Locks locks)
	{
		var world = new BeatmapsetWorld();
		var app = world.CreateApplication();
		await app.SetSettingsAsync(new ServerSettings { Locks = locks });
		var by = await app.LoginAsync(await app.CreateUserAsync("Uploader", "password", permissions));
		return (app, world, app.Server.ScopeTo(by).Beatmapsets);
	}

	[Fact]
	public async Task UploadAsync_WhenUnlocked_StoresTheSet()
	{
		var (app, world, uploads) = await SetupAsync(UserPermissionsGroups.Default, Locks.None);
		using var _ = app;

		var (result, import) = await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Null(import!.Failure);
		Assert.Single(world.Sets.Records);
	}

	[Fact]
	public async Task UploadAsync_WithoutTheUploadPermission_NamesIt()
	{
		var (app, world, uploads) = await SetupAsync(
			UserPermissionsGroups.Default & ~UserPermissions.BeatmapsetUpload, Locks.None);
		using var _ = app;

		var (result, import) = await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)),
			TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPermissions.BeatmapsetUpload, result.Missing);
		Assert.Null(import);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task UploadAsync_WhileLocked_RefusesAUserWithoutBeatmapsetManage()
	{
		var (app, world, uploads) = await SetupAsync(UserPermissionsGroups.Default, Locks.Beatmapsets);
		using var _ = app;

		var (result, import) = await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Locked, result.Outcome);
		Assert.Null(import);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task UploadAsync_WhileLocked_LetsABeatmapsetManagerUpload()
	{
		var (app, world, uploads) = await SetupAsync(
			UserPermissionsGroups.Default | UserPermissions.BeatmapsetManage, Locks.Beatmapsets);
		using var _ = app;

		var (result, _) = await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Single(world.Sets.Records);
	}

	[Fact]
	public async Task UploadAsync_OfASetTheServerHas_IsRejectedAndTheStoredSetStays()
	{
		var (app, world, uploads) = await SetupAsync(UserPermissionsGroups.Default, Locks.None);
		using var _ = app;
		await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)), TestContext.Current.CancellationToken);

		var (result, import) = await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Equal(BeatmapsetImportFailure.AlreadyExists, import!.Failure);
		Assert.Single(world.Sets.Records);
	}
}
