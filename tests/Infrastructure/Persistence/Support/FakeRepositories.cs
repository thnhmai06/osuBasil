using Basil.Application.Persistence.Beatmaps;
using Basil.Application.Persistence.Common;
using Basil.Application.Persistence.Users;
using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Users;

namespace Basil.Infrastructure.Persistence.Tests.Support;

/// <summary>Stores one user in memory, for tests that drive only the lookups of a repository.</summary>
internal sealed class FakeUserRepository : IUserRepository
{
	public FakeUserRepository(UserRecord? user) => User = user;

	public UserRecord? User { get; set; }

	public ValueTask<UserRecord?> GetAsync(int id, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(User is { } found && found.Id == id ? found : null);

	public Task<UserRecord> CreateAsync(UserData data, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task<UserRecord> ReplaceAsync(int id, UserData data, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task<bool> RenameAsync(UserRecord user, string name, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask<UserRecord?> GetByNameAsync(string name, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task<Page<UserRecord>> ListAsync(UserQuery query, PageRequest page,
		CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();
}

/// <summary>Stores one beatmap in memory, for tests that drive only the lookups of a repository.</summary>
internal sealed class FakeBeatmapRepository : IBeatmapRepository
{
	public FakeBeatmapRepository(BeatmapRecord? beatmap) => Beatmap = beatmap;

	public BeatmapRecord? Beatmap { get; set; }

	public ValueTask<BeatmapRecord?> GetAsync(Md5 hash, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(Beatmap is { } found && found.Hash == hash ? found : null);

	public Task UpdateAsync(BeatmapRecord beatmap, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public ValueTask<BeatmapRecord?> GetAsync(int id, CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(Beatmap is { } found && found.Id == id ? found : null);

	public Task<IReadOnlyList<BeatmapRecord>> ListAsync(BeatmapsetRecord set,
		CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task<Page<BeatmapRecord>> ListAsync(BeatmapQuery query, PageRequest page,
		CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	public Task RetainAsync(BeatmapsetRecord set, IReadOnlyCollection<BeatmapRecord> keep,
		CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();
}
