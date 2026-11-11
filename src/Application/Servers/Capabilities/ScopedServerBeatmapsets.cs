using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>Uploads beatmapsets to the server for one connection.</summary>
public readonly struct ScopedServerBeatmapsets
{
	private readonly ScopedServer _scoped;

	/// <summary>Creates the upload operations scoped to a connection.</summary>
	/// <param name="scoped">The server scope.</param>
	internal ScopedServerBeatmapsets(ScopedServer scoped)
	{
		_scoped = scoped;
	}

	/// <summary>Uploads a beatmapset archive as a new beatmapset.</summary>
	/// <param name="archive">The .osz archive of one beatmapset, read from its current position; not disposed.</param>
	/// <param name="ct">A token that cancels the operation.</param>
	/// <returns>The outcome and, when the upload was allowed, the result of the import.</returns>
	/// <remarks>
	///     Uploading needs <see cref="UserPrivileges.Player" />. While <see cref="Locks.Beatmapsets" /> is
	///     locked, only a user holding <see cref="UserPrivileges.Tournament" /> may upload. A beatmapset the server
	///     already has is never overwritten: the upload is rejected with
	///     <see cref="BeatmapsetImportFailure.AlreadyExists" />. An archive larger than the upload limit of the
	///     server settings is refused with <see cref="ServerOutcome.TooLarge" />.
	/// </remarks>
	public async Task<(ServerOutcome Result, BeatmapsetImportResult? Import)> UploadAsync(Stream archive,
		CancellationToken ct = default)
	{
		var server = _scoped.Server;
		if (await server.Beatmapsets.IsLockedAsync(Locks.Beatmapsets, ct) &&
		    _scoped.Lacks(UserPrivileges.Tournament))
			return (ServerOutcome.Locked, null);
		if (_scoped.Lacks(UserPrivileges.Player))
			return (ServerOutcome.Denied, null);

		return await server.Beatmapsets.UploadAsync(archive, ct);
	}
}