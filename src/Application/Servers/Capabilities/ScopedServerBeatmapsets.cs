using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common.Common;
using Basil.Application.Content.Persistence;
using Basil.Application.Servers.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Servers.Capabilities;

/// <summary>Uploads beatmapsets to the server for one connection.</summary>
public sealed class ScopedServerBeatmapsets
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
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome and, when the upload was allowed, the result of the import.</returns>
	/// <remarks>
	///     Uploading needs <see cref="UserPermissions.BeatmapsetUpload" />. While <see cref="Locks.Beatmapsets" /> is
	///     locked, only a user holding <see cref="UserPermissions.BeatmapsetManage" /> may upload. A beatmapset the server
	///     already has is never overwritten: the upload is rejected with
	///     <see cref="BeatmapsetImportFailure.AlreadyExists" />.
	/// </remarks>
	public async Task<(Result<ServerOutcome> Result, BeatmapsetImportResult? Import)> UploadAsync(Stream archive,
		CancellationToken cancellationToken = default)
	{
		var context = _scoped.Server.Context;
		if ((await context.Settings.GetAsync(cancellationToken)).Locks.HasFlag(Locks.Beatmapsets) &&
		    _scoped.Needs(UserPermissions.BeatmapsetManage) is not null)
			return (ServerOutcome.Locked, null);
		if (_scoped.Needs(UserPermissions.BeatmapsetUpload) is { } missing)
			return (Result<ServerOutcome>.Denied(missing), null);

		return (ServerOutcome.Ok, await context.Beatmapsets.ImportAsync(archive, false, cancellationToken));
	}
}