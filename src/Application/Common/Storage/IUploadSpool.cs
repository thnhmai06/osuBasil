namespace Basil.Application.Common.Storage;

/// <summary>
///     Takes an upload off the request into a file, so that it can be checked and read again without being held in
///     memory.
/// </summary>
public interface IUploadSpool
{
	/// <summary>Reads an upload into a file, stopping one byte past the limit.</summary>
	/// <param name="content">The upload, read from its current position to its end; not disposed.</param>
	/// <param name="maxBytes">The most bytes the upload may have.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The spooled upload, to be disposed by the caller.</returns>
	Task<ISpooledUpload> SpoolAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default);
}

/// <summary>An upload held in a file that exists until it is disposed.</summary>
public interface ISpooledUpload : IAsyncDisposable
{
	/// <summary>Gets a seekable stream over the upload, positioned at its start; cut one byte past the limit.</summary>
	Stream Content { get; }

	/// <summary>Gets a value indicating whether the upload had more bytes than the limit.</summary>
	bool TooLarge { get; }

	/// <summary>Keeps an upload that failed validation in quarantine, for the operator to look at, instead of deleting it.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <remarks>The upload is no longer readable afterwards. Quarantined files older than seven days are deleted.</remarks>
	Task QuarantineAsync(CancellationToken cancellationToken = default);
}