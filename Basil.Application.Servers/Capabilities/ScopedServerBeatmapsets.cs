using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

public readonly struct ScopedServerBeatmapsets
{
	private readonly ScopedServer _scoped;

	internal ScopedServerBeatmapsets(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	public async Task<(ServerActionResult Result, BeatmapsetImportResult? Import)> UploadAsync(Stream archive,
		CancellationToken ct = default)
	{
		var server = _scoped.Server;
		if (await server.Beatmapsets.IsLockedAsync(Locks.Beatmapsets, ct) &&
		    _scoped.Lacks(UserPrivileges.Tournament))
			return (ServerActionResult.Locked, null);
		if (_scoped.Lacks(UserPrivileges.Player))
			return (ServerActionResult.Denied, null);

		return await server.Beatmapsets.UploadAsync(archive, ct);
	}
}