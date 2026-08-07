using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Abstractions.Settings;
using Basil.Application.Abstractions.Users;
using Basil.Domain.Beatmaps;
using Basil.Domain.Channels;
using Basil.Domain.Users;
using Basil.Infrastructure.Security;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Shared NSubstitute-backed test doubles for the endpoint tests. Only the truly identical
///     shapes live here — a repository/service with divergent per-file behavior stays a local
///     <c>Substitute.For&lt;T&gt;()</c> configured with just what that file's tests need.
/// </summary>
internal static class TestDoubles
{
	/// <summary>No channels — used where routing doesn't care about channels.</summary>
	public static IChannelRepository NullChannelRepository()
	{
		var repo = Substitute.For<IChannelRepository>();
		repo.FetchAllAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<Channel>>([]));
		repo.FetchOneByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<Channel?>(null));
		return repo;
	}

	/// <summary>Verifies against a single fixed "correct-md5" digest — used where a route needs a password check to succeed/fail deterministically without real bcrypt.</summary>
	public static IPasswordHasher FixedPasswordHasher()
	{
		var hasher = Substitute.For<IPasswordHasher>();
		hasher.Hash(Arg.Any<byte[]>()).Returns(_ => throw new NotSupportedException());
		hasher.Verify(Arg.Any<byte[]>(), Arg.Any<string>())
			.Returns(call => Encoding.UTF8.GetString(call.ArgAt<byte[]>(0)) == "correct-md5");
		return hasher;
	}

	/// <summary>Every beatmap unknown, upsert/search all no-ops.</summary>
	public static IBeatmapRepository NullMapRepository()
	{
		var repo = Substitute.For<IBeatmapRepository>();
		repo.FetchOneAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
			Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Beatmap?>(null));
		repo.UpsertAsync(Arg.Any<Beatmap>(), Arg.Any<CancellationToken>())
			.Returns(call => Task.FromResult(call.ArgAt<Beatmap>(0)));
		repo.SearchAsync(Arg.Any<string?>(), Arg.Any<GameMode?>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<IReadOnlyList<Beatmap>>>([]));
		repo.FetchAllBySetIdAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<Beatmap>>([]));
		return repo;
	}

	/// <summary>Every beatmapset unknown — used where routing doesn't care about beatmapsets.</summary>
	public static IBeatmapsetRepository NullMapsetRepository()
	{
		var repo = Substitute.For<IBeatmapsetRepository>();
		repo.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Beatmapset?>(null));
		return repo;
	}

	/// <summary>Every user unknown, every write a no-op.</summary>
	public static IUserRepository NullUserRepository()
	{
		var repo = Substitute.For<IUserRepository>();
		repo.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<User?>(null));
		repo.FetchByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<User?>(null));
		repo.FetchPasswordHashAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<string?>(null));
		repo.FetchAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<User>>([]));
		return repo;
	}

	/// <summary>
	///     A stored admin key hash of <paramref name="correctKey" />, standing in for the real
	///     Settings-table-backed repository in tests that run without a database
	///     (<c>DatabaseOptions.Path == ""</c>) — used wherever a route only needs the admin-key gate
	///     to work deterministically against one fixed key.
	/// </summary>
	public static ISettingsRepository FixedAdminKeySettingsRepository(string correctKey = "correct-key")
	{
		var repo = Substitute.For<ISettingsRepository>();
		var hash = new BCryptPasswordHasher().Hash(Encoding.UTF8.GetBytes(correctKey));
		repo.GetAsync("AdminKey:Hash", Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(hash));
		repo.GetAsync("AdminKey:LastChanged", Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<string?>(DateTimeOffset.UtcNow.ToString("O")));
		return repo;
	}

	/// <summary>
	///     No admin key hash stored — puts the server in bypass mode, where every admin-gated action
	///     and in-game registration succeeds without a key.
	/// </summary>
	public static ISettingsRepository BypassAdminKeySettingsRepository()
	{
		var repo = Substitute.For<ISettingsRepository>();
		repo.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
		return repo;
	}
}