using Basil.Domain.Beatmaps;

using Basil.Domain.Game.Mechanics;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Multiplayer;

public sealed record BeatmapReference(Md5Hash Hash, int Id, string Name, GameMode Mode, BeatmapRecord? Known);