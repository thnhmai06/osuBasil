namespace Basil.Application.Shared.Storage;

/// <summary>A stored picture ready to be served: its content, its media type and when it last changed.</summary>
/// <param name="Content">The picture's bytes, positioned at the start.</param>
/// <param name="ContentType">The media type of the picture.</param>
/// <param name="LastModified">When the picture last changed.</param>
public sealed record ImageFile(Stream Content, string ContentType, DateTimeOffset LastModified) : IAsyncDisposable
{
	public ValueTask DisposeAsync()
	{
		return Content.DisposeAsync();
	}
}
