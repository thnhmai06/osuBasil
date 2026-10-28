namespace Basil.Application.Servers.Runtime;

/// <summary>What happened to a server operation that was not refused for lack of permissions.</summary>
public enum ServerOutcome : byte
{
	/// <summary>The operation succeeded.</summary>
	Ok = 1,

	/// <summary>A channel with that name is already open.</summary>
	NameTaken,

	/// <summary>The screenshot is larger than the allowed size in bytes.</summary>
	TooLarge,

	/// <summary>The content is not a screenshot picture the server accepts.</summary>
	InvalidImage,

	/// <summary>The screenshot is wider or taller than the allowed number of pixels.</summary>
	DimensionsTooLarge,

	/// <summary>The area is locked and the caller does not manage it.</summary>
	Locked
}