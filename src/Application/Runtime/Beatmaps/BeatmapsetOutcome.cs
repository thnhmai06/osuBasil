namespace Basil.Application.Runtime.Beatmaps;

/// <summary>What happened to a beatmapset operation that was not refused for lack of permissions.</summary>
public enum BeatmapsetOutcome : byte
{
	/// <summary>The operation succeeded.</summary>
	Ok = 1,

	/// <summary>The set is write-locked and cannot be deleted.</summary>
	Locked
}