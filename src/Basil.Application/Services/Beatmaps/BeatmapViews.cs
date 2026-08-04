using Basil.Domain.Beatmaps;

namespace Basil.Application.Services.Beatmaps;

/// <summary>
///     Shared shape for every beatmap embed or response.
/// </summary>
/// <param name="Md5">The MD5 hash of the beatmap file's contents.</param>
/// <param name="Id">The osu! id of the beatmap.</param>
/// <param name="Version">The difficulty name, such as "Insane".</param>
/// <param name="Difficulty">The gameplay stats of the beatmap.</param>
/// <param name="ObjectCounts">The per-mode hit-object counts of the beatmap.</param>
/// <param name="IsLocallyIngested">Whether the beatmap was ingested without a real osu! online id.</param>
/// <remarks>
///     Never carries the beatmap's internal filename or a parent beatmapset reference. The split
///     between <see cref="BeatmapInSet" /> and <see cref="BeatmapDetail" /> keeps a beatmap from
///     referencing its set, which references the beatmap again.
///     <see cref="Domain.Beatmaps.Difficulty.TotalLength" /> serializes as a whole number of seconds.
/// </remarks>
public abstract record BeatmapView(
	string Md5,
	int Id,
	string Version,
	Difficulty Difficulty,
	BeatmapObjectCounts ObjectCounts,
	bool IsLocallyIngested);

/// <summary>
///     A beatmap embed that carries no parent beatmapset reference.
/// </summary>
/// <remarks>
///     Used inside <see cref="BeatmapsetDetail" />'s <c>Beatmaps</c> list. Omitting the parent
///     beatmapset avoids the beatmap-in-set-in-beatmap cycle.
/// </remarks>
public sealed record BeatmapInSet(
	string Md5,
	int Id,
	string Version,
	Difficulty Difficulty,
	BeatmapObjectCounts ObjectCounts,
	bool IsLocallyIngested)
	: BeatmapView(Md5, Id, Version, Difficulty, ObjectCounts, IsLocallyIngested);

/// <summary>
///     A beatmap embed that carries its parent beatmapset.
/// </summary>
/// <param name="Md5">The MD5 hash of the beatmap file's contents.</param>
/// <param name="Id">The osu! id of the beatmap.</param>
/// <param name="Version">The difficulty name, such as "Insane".</param>
/// <param name="Difficulty">The gameplay stats of the beatmap.</param>
/// <param name="ObjectCounts">The per-mode hit-object counts of the beatmap.</param>
/// <param name="IsLocallyIngested">Whether the beatmap was ingested without a real osu! online id.</param>
/// <param name="Beatmapset">The beatmapset summary the beatmap belongs to.</param>
/// <remarks>
///     Used everywhere a beatmap appears outside a beatmapset's own <c>Beatmaps</c> list, such as a
///     score, a match round, a live snapshot, or the beatmap detail endpoint. The embedded summary
///     omits the set's own beatmap list, which avoids the beatmap-in-set-in-beatmap cycle.
/// </remarks>
public sealed record BeatmapDetail(
	string Md5,
	int Id,
	string Version,
	Difficulty Difficulty,
	BeatmapObjectCounts ObjectCounts,
	bool IsLocallyIngested,
	BeatmapsetSummary Beatmapset)
	: BeatmapView(Md5, Id, Version, Difficulty, ObjectCounts, IsLocallyIngested);

/// <summary>
///     API-facing summary of a beatmapset.
/// </summary>
/// <param name="Id">The unique identifier of the set.</param>
/// <param name="Artist">The artist of the set's music.</param>
/// <param name="Title">The title of the set's music.</param>
/// <param name="Creator">The username of the set's creator.</param>
/// <param name="LastUpdate">The time of the latest re-ingestion or content change, in UTC.</param>
/// <param name="CreatedAt">The time the set was first ingested, in UTC.</param>
/// <param name="IsFrozen">Whether the set is write-locked by an admin.</param>
/// <param name="IsPrivate">
///     Whether the set is hidden from non-admin listings and the public beatmap endpoints.
/// </param>
/// <param name="Status">The ranked status of the set.</param>
/// <param name="BeatmapCount">The number of difficulties in the set.</param>
/// <remarks>
///     Used both as a list item for the beatmapset list endpoint and as the parent embed on
///     <see cref="BeatmapDetail" />.
/// </remarks>
public sealed record BeatmapsetSummary(
	int Id,
	string Artist,
	string Title,
	string Creator,
	DateTime LastUpdate,
	DateTime CreatedAt,
	bool IsFrozen,
	bool IsPrivate,
	BeatmapStatus Status,
	int BeatmapCount);

/// <summary>
///     The full payload for a single beatmapset.
/// </summary>
/// <param name="Id">The unique identifier of the set.</param>
/// <param name="Artist">The artist of the set's music.</param>
/// <param name="Title">The title of the set's music.</param>
/// <param name="Creator">The username of the set's creator.</param>
/// <param name="LastUpdate">The time of the latest re-ingestion or content change, in UTC.</param>
/// <param name="CreatedAt">The time the set was first ingested, in UTC.</param>
/// <param name="IsFrozen">Whether the set is write-locked by an admin.</param>
/// <param name="IsPrivate">
///     Whether the set is hidden from non-admin listings and the public beatmap endpoints.
/// </param>
/// <param name="Status">The ranked status of the set.</param>
/// <param name="Beatmaps">Every difficulty under the set, each as a <see cref="BeatmapInSet" />.</param>
/// <remarks>
///     Returned by the beatmapset detail endpoint, with every difficulty embedded as a
///     <see cref="BeatmapInSet" /> rather than a <see cref="BeatmapDetail" /> to avoid carrying a
///     parent beatmapset per difficulty.
/// </remarks>
public sealed record BeatmapsetDetail(
	int Id,
	string Artist,
	string Title,
	string Creator,
	DateTime LastUpdate,
	DateTime CreatedAt,
	bool IsFrozen,
	bool IsPrivate,
	BeatmapStatus Status,
	IReadOnlyList<BeatmapInSet> Beatmaps);

/// <summary>
///     Maps the domain <see cref="Beatmapset" /> and <see cref="Beatmap" /> records onto the API-facing
///     view types in this file.
/// </summary>
public static class BeatmapViewMapper
{
	/// <param name="beatmapset">The set to map.</param>
	extension(Beatmapset beatmapset)
	{
		/// <summary>
		///     Maps a <see cref="Beatmapset" /> to a <see cref="BeatmapsetSummary" />.
		/// </summary>
		/// <param name="beatmapCount">The number of difficulties in the set.</param>
		/// <returns>A summary of the set.</returns>
		public BeatmapsetSummary ToSummary(int beatmapCount)
		{
			return new BeatmapsetSummary(beatmapset.Id, beatmapset.Artist, beatmapset.Title, beatmapset.Creator,
				beatmapset.LastUpdate,
				beatmapset.CreatedAt, beatmapset.IsFrozen, beatmapset.IsPrivate, Beatmapset.Status, beatmapCount);
		}

		/// <summary>
		///     Maps a <see cref="Beatmapset" /> and its difficulties to a <see cref="BeatmapsetDetail" />.
		/// </summary>
		/// <param name="beatmaps">The difficulties under the set.</param>
		/// <returns>A detail view of the set.</returns>
		public BeatmapsetDetail ToDetail(IReadOnlyList<Beatmap> beatmaps)
		{
			return new BeatmapsetDetail(beatmapset.Id, beatmapset.Artist, beatmapset.Title, beatmapset.Creator,
				beatmapset.LastUpdate,
				beatmapset.CreatedAt, beatmapset.IsFrozen, beatmapset.IsPrivate, Beatmapset.Status,
				[.. beatmaps.Select(b => b.ToInSet())]);
		}
	}

	/// <param name="beatmap">The difficulty to map.</param>
	extension(Beatmap beatmap)
	{
		/// <summary>
		///     Maps a <see cref="Beatmap" /> to a <see cref="BeatmapInSet" />.
		/// </summary>
		/// <returns>An embed of the difficulty without its parent set.</returns>
		public BeatmapInSet ToInSet()
		{
			return new BeatmapInSet(beatmap.Md5, beatmap.Id, beatmap.Version, beatmap.Difficulty,
				beatmap.ObjectCounts,
				beatmap.IsLocallyIngested);
		}

		/// <summary>
		///     Maps a <see cref="Beatmap" /> and its parent set summary to a <see cref="BeatmapDetail" />.
		/// </summary>
		/// <param name="beatmapset">The summary of the set the difficulty belongs to.</param>
		/// <returns>An embed of the difficulty with its parent set.</returns>
		public BeatmapDetail ToDetail(BeatmapsetSummary beatmapset)
		{
			return new BeatmapDetail(beatmap.Md5, beatmap.Id, beatmap.Version, beatmap.Difficulty,
				beatmap.ObjectCounts,
				beatmap.IsLocallyIngested, beatmapset);
		}
	}
}