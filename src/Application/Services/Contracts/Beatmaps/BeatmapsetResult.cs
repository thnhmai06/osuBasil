namespace Basil.Application.Services.Contracts.Beatmaps;

/// <summary>The outcome of a beatmapset operation.</summary>
public enum BeatmapsetResult
{
	/// <summary>The operation succeeded.</summary>
	Ok,

	/// <summary>The caller has never been granted the required permission.</summary>
	NotAuthorized,

	/// <summary>The caller holds the permission, but an active restriction suspends it.</summary>
	Restricted,

	/// <summary>The set is write-locked and cannot be deleted.</summary>
	Locked
}