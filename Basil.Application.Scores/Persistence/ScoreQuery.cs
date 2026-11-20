using Basil.Domain.Game;
using Basil.Domain.Users;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Scores.Persistence;

public sealed record ScoreQuery(
	UserRecord? Player = null,
	Md5Hash? BeatmapHash = null,
	GameMods? Mods = null,
	IReadOnlySet<UserRecord>? Players = null,
	bool BestPerPlayer = false,
	IReadOnlySet<int>? UserIds = null,
	IReadOnlySet<Md5Hash>? BeatmapHashes = null,
	bool PassedOnly = false,
	GameMode? Mode = null);