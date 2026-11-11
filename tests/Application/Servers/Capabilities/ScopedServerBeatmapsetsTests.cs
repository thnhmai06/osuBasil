using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common.Settings;
using Basil.Application.Servers.Capabilities;
using Basil.Application.Servers.Objects;
using Basil.Domain.Users;
using Basil.Testing;
using static Basil.Testing.BeatmapsetWorld;

namespace Basil.Application.Servers.Tests.Capabilities;

/// <summary>
///     Uploading a beatmapset as a connection, from the XML contract of <see cref="ScopedServerBeatmapsets" />: it needs
///     <see cref="UserPrivileges.Player" />, a locked area refuses everyone but managers, and a set the
///     server has is never overwritten.
/// </summary>
public sealed class ScopedServerBeatmapsetsTests
{
	private static readonly Diff Easy = new("Easy", "easy file");

	private static async Task<(TestApplication App, BeatmapsetWorld World, ScopedServerBeatmapsets Uploads)> SetupAsync(
		UserPrivileges privileges, Locks locks)
	{
		var world = new BeatmapsetWorld();
		var app = world.CreateApplication();
		await app.SetSettingsAsync(new ServerSettings { Locks = locks });
		var actor = await app.LoginAsync(await app.CreateUserAsync("Uploader", "password", privileges));
		return (app, world, app.Server.ScopeTo(actor).Beatmapsets);
	}

	[Fact]
	public async Task WhenUnlocked_StoresTheSet()
	{
		var (app, world, uploads) = await SetupAsync(UserPrivilegesExtensions.Default, Locks.None);
		using var _ = app;

		var (result, import) = await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Null(import!.Failure);
		Assert.Single(world.Sets.Records);
	}

	[Fact]
	public async Task OverLimitArchive_TooLargeUnstored()
	{
		var (app, world, uploads) = await SetupAsync(UserPrivilegesExtensions.Default, Locks.None);
		using var _ = app;
		var bytes = Osz.Build(100, Easy);
		await app.SetSettingsAsync(new ServerSettings
			{ Locks = Locks.None, Uploads = new ServerSettings.UploadSettings { OszMaxSize = bytes.Length - 1 } });

		var (result, import) = await uploads.UploadAsync(new MemoryStream(bytes),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.TooLarge, result.Outcome);
		Assert.Null(import);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task ArchiveAtLimit_StoresSet()
	{
		var (app, world, uploads) = await SetupAsync(UserPrivilegesExtensions.Default, Locks.None);
		using var _ = app;
		var bytes = Osz.Build(100, Easy);
		await app.SetSettingsAsync(new ServerSettings
			{ Locks = Locks.None, Uploads = new ServerSettings.UploadSettings { OszMaxSize = bytes.Length } });

		var (result, _) = await uploads.UploadAsync(new MemoryStream(bytes), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Single(world.Sets.Records);
	}

	[Fact]
	public async Task NoUploadPrivilege_NamesIt()
	{
		var (app, world, uploads) = await SetupAsync(
			UserPrivilegesExtensions.Default & ~UserPrivileges.Player, Locks.None);
		using var _ = app;

		var (result, import) = await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)),
			TestContext.Current.CancellationToken);

		Assert.False(result.IsOk);
		Assert.Equal(UserPrivileges.Player, result.Missing);
		Assert.Null(import);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task Locked_RefusesNonManager()
	{
		var (app, world, uploads) = await SetupAsync(UserPrivilegesExtensions.Default, Locks.Beatmapsets);
		using var _ = app;

		var (result, import) = await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Locked, result.Outcome);
		Assert.Null(import);
		Assert.Empty(world.Sets.Records);
	}

	[Fact]
	public async Task Locked_ManagerUploads()
	{
		var (app, world, uploads) = await SetupAsync(
			UserPrivilegesExtensions.Default | UserPrivileges.Tournament, Locks.Beatmapsets);
		using var _ = app;

		var (result, _) = await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Single(world.Sets.Records);
	}

	[Fact]
	public async Task ExistingSet_RejectedSetStays()
	{
		var (app, world, uploads) = await SetupAsync(UserPrivilegesExtensions.Default, Locks.None);
		using var _ = app;
		await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)), TestContext.Current.CancellationToken);

		var (result, import) = await uploads.UploadAsync(new MemoryStream(Osz.Build(100, Easy)),
			TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, result.Outcome);
		Assert.Equal(BeatmapsetImportFailure.AlreadyExists, import!.Failure);
		Assert.Single(world.Sets.Records);
	}
}