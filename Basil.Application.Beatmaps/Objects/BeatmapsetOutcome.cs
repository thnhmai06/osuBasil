namespace Basil.Application.Beatmaps.Objects;

public enum BeatmapsetOutcome : byte
{
	Denied = 0,

	Ok = 1,

	Locked,

	NotFound
}