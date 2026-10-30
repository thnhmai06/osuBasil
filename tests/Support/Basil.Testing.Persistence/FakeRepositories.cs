using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
using Basil.Application.Common.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Testing.Persistence;

/// <summary>Stores one user in memory, for tests that drive only the lookups of a repository.</summary>
public sealed class FakeUserRepository(UserRecord? user) : IUserRepository
{
	public UserRecord? User { get; set; } = user;

	public ValueTask<UserRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(User is { } found && found.Id == id ? found : null);
	}

	public Task<UserRecord> CreateAsync(UserData data, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task<UserRecord> ReplaceAsync(int id, UserData data, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task<bool> RenameAsync(UserRecord user, string name, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public ValueTask<UserRecord?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task<Page<UserRecord>> ListAsync(UserQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}
}

/// <summary>Stores one beatmap in memory, for tests that drive only the lookups of a repository.</summary>
public sealed class FakeBeatmapRepository(BeatmapRecord? beatmap) : IBeatmapRepository
{
	public BeatmapRecord? Beatmap { get; set; } = beatmap;

	public ValueTask<BeatmapRecord?> GetAsync(Md5 hash, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(Beatmap is { } found && found.Hash == hash ? found : null);
	}

	public Task UpdateAsync(BeatmapRecord beatmap, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public ValueTask<BeatmapRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(Beatmap is { } found && found.Id == id ? found : null);
	}

	public Task<IReadOnlyList<BeatmapRecord>> ListAsync(BeatmapsetRecord set,
		CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task<Page<BeatmapRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public Task RetainAsync(BeatmapsetRecord set, IReadOnlyCollection<BeatmapRecord> keep,
		CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}
}