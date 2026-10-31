namespace Basil.Protocol.Bancho.Models.Beatmaps;

/// <summary>Describes one beatmap in the beatmap-info-reply packet.</summary>
/// <param name="Index">The position of the beatmap in the request that this entry answers.</param>
/// <param name="MapId">The id of the beatmap.</param>
/// <param name="MapSetId">The id of the beatmapset the beatmap belongs to.</param>
/// <param name="ThreadId">The id of the beatmapset's forum thread.</param>
/// <param name="RankedStatus">The ranked status of the beatmap as the client numbers it.</param>
/// <param name="GradeOsu">The player's best grade on the osu!standard difficulty, as the client numbers grades.</param>
/// <param name="GradeCatch">The player's best grade on the osu!catch difficulty.</param>
/// <param name="GradeTaiko">The player's best grade on the osu!taiko difficulty.</param>
/// <param name="GradeMania">The player's best grade on the osu!mania difficulty.</param>
/// <param name="Checksum">The md5 checksum of the beatmap file.</param>
public sealed record BeatmapInfoReplyEntry(
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