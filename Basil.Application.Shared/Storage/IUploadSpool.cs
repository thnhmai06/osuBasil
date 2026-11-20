namespace Basil.Application.Shared.Storage;

public interface IUploadSpool
{
	Task<ISpooledUpload> SpoolAsync(Stream content, long maxBytes, CancellationToken ct = default);
}

public interface ISpooledUpload : IAsyncDisposable
{
	Stream Content { get; }

	bool TooLarge { get; }

	Task QuarantineAsync(CancellationToken ct = default);
}