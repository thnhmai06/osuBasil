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

	public async Task<ServerActionResult> MayUploadAsync(CancellationToken ct = default)
	{
		if (await _scoped.Server.Beatmapsets.IsLockedAsync(Locks.Beatmapsets, ct) &&
		    _scoped.Lacks(UserPrivileges.Tournament))
			return ServerActionResult.Locked;

		return _scoped.Lacks(UserPrivileges.Player) ? ServerActionResult.Denied : ServerActionResult.Ok;
	}

	public async Task<(ServerActionResult Result, BeatmapsetImportResult? Import)> UploadAsync(Stream archive,
		CancellationToken ct = default)
	{
		var refusal = await MayUploadAsync(ct);
		if (refusal != ServerActionResult.Ok) return (refusal, null);

		return await _scoped.Server.Beatmapsets.UploadAsync(archive, ct);
	}
}