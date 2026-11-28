namespace Basil.Host.Shared.Http;

/// <summary>Why an upload was cut off before it was accepted.</summary>
public enum UploadRefusal : byte
{
	/// <summary>More bytes than the limit of that kind of upload.</summary>
	TooLarge,

	/// <summary>The sender is slower than the minimum data rate.</summary>
	TooSlow,

	/// <summary>The content does not begin the way that kind of upload must.</summary>
	WrongContent,

	/// <summary>The body is not a well-formed form within the bounds of an upload.</summary>
	Malformed
}

/// <summary>Raised while an upload is read when it must be refused and its connection cut.</summary>
public sealed class UploadRefusedException(UploadRefusal reason) : Exception
{
	/// <summary>Why the upload is refused.</summary>
	public UploadRefusal Reason { get; } = reason;
}
