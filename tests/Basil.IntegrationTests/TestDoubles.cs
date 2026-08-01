using System.Text;
using Basil.Application.Abstractions.Beatmaps;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Abstractions.Users;
using Basil.Domain.Beatmaps;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.IntegrationTests;

/// <summary>
///     Shared NSubstitute-backed test doubles for the endpoint tests that were previously
///     copy-pasting the same byte-identical no-op stub class into every file. Only the truly
///     identical shapes live here — a repository/service with divergent per-file behavior stays a
///     local <c>Substitute.For&lt;T&gt;()</c> configured with just what that file's tests need.
/// </summary>
internal static class TestDoubles
{
	/// <summary>No channels, matching the old NullChannelRepository — used where routing doesn't care about channels.</summary>
	public static IChannelRepository NullChannelRepository()
	{
		var repo = Substitute.For<IChannelRepository>();
		repo.FetchAllAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<IReadOnlyList<Channel>>([]));
		repo.FetchOneByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<Channel?>(null));
		return repo;
	}

	/// <summary>
	///     Verifies against a single fixed "correct-md5" digest, matching the old StubPasswordHasher —
	///     used where a route needs a password check to succeed/fail deterministically without real bcrypt.
	/// </summary>
	public static IPasswordHasher FixedPasswordHasher()
	{
		var hasher = Substitute.For<IPasswordHasher>();
		hasher.Hash(Arg.Any<byte[]>()).Returns(_ => throw new NotSupportedException());
		hasher.Verify(Arg.Any<byte[]>(), Arg.Any<string>())
			.Returns(call => Encoding.UTF8.GetString(call.ArgAt<byte[]>(0)) == "correct-md5");
		return hasher;
	}

	/// <summary>Every beatmap unknown, upsert/search all no-ops — matching the old NullMapRepository.</summary>
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

	/// <summary>Every mapset unknown — used where routing doesn't care about beatmapsets.</summary>
	public static IMapsetRepository NullMapsetRepository()
	{
		var repo = Substitute.For<IMapsetRepository>();
		repo.FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Mapset?>(null));
		return repo;
	}

	/// <summary>Every user unknown, every write a no-op, matching the old StubUserRepository used as a pure DI placeholder.</summary>
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
}