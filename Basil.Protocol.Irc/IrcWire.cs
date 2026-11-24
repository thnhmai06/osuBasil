namespace Basil.Protocol.Irc;

/// <summary>The size limits and framing of an IRC line.</summary>
public static class IrcWire
{
	/// <summary>The terminator of every line on the wire.</summary>
	public const string LineEnd = "\r\n";

	/// <summary>The most bytes a line may carry without its message tags.</summary>
	public const int MaxBodyBytes = 512;

	/// <summary>The most bytes the message tags of a line may carry.</summary>
	public const int MaxTagBytes = 8191;

	/// <summary>The most bytes a line may carry with its message tags.</summary>
	public const int MaxLineBytes = MaxTagBytes + MaxBodyBytes;

	/// <summary>The most bytes one line of a long text or of a name list may carry, leaving room for the rest of the line.</summary>
	public const int MaxChunkBytes = 400;

	/// <summary>The most characters a chat message may carry.</summary>
	public const int MaxMessageLength = 450;
}
