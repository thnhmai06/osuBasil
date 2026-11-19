using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Domain.Multiplayer.Room;

public sealed record BeatmapReference(Md5Hash Hash, int Id, string Name, GameMode Mode, BeatmapRecord? Known);