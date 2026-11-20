namespace Basil.Application.Beatmaps.Objects;

public enum BeatmapsetEditResult : byte
{
	Denied = 0,

	Ok = 1,

	Locked,

	NotFound
}