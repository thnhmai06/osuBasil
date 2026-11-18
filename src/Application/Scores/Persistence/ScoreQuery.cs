
using Basil.Domain.Mechanics;
using Basil.Domain.Users;

namespace Basil.Application.Scores.Persistence;

public sealed record ScoreQuery(
	UserRecord? Player = null,
	Md5? BeatmapHash = null,
	GameMods? Mods = null,
	IReadOnlySet<UserRecord>? Players = null,
	bool BestPerPlayer = false,
	IReadOnlySet<int>? UserIds = null,
	IReadOnlySet<Md5>? BeatmapHashes = null,
	bool PassedOnly = false,
	GameMode? Mode = null);