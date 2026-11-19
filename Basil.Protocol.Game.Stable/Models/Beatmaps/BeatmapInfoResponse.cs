namespace Basil.Protocol.Game.Stable.Models.Beatmaps;

public sealed record BeatmapInfoResponse(
	short Index,
	int MapId,
	int MapSetId,
	int ThreadId,
	byte RankedStatus,
	sbyte GradeOsu,
	sbyte GradeCatch,
	sbyte GradeTaiko,
	sbyte GradeMania,
	string Checksum);