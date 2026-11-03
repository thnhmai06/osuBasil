using Basil.Application.Beatmaps.Persistence;
using Basil.Application.Common;
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
	///     <see cref="BeatmapsetImportFailure.AlreadyExists" />. An archive larger than the upload limit of the
	///     server settings is refused with <see cref="ServerOutcome.TooLarge" />.
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

		var limit = (await context.Settings.GetAsync(cancellationToken)).Uploads.OszMaxSize;
		try
		{
			return (ServerOutcome.Ok,
				await context.Beatmapsets.ImportAsync(new CappedStream(archive, limit), false, cancellationToken));
		}
		catch (ArchiveTooLargeException)
		{
			return (ServerOutcome.TooLarge, null);
		}
	}

	private sealed class ArchiveTooLargeException : Exception;

	/// <summary>Reads another stream and fails once more than a limit of bytes has been read.</summary>
	private sealed class CappedStream(Stream inner, long limit) : Stream
	{
		private long _read;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			return Counted(inner.Read(buffer, offset, count));
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
			CancellationToken cancellationToken = default)
		{
			return Counted(await inner.ReadAsync(buffer, cancellationToken));
		}

		private int Counted(int read)
		{
			_read += read;
			return _read > limit ? throw new ArchiveTooLargeException() : read;
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}
	}
}