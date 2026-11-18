using Basil.Domain.Beatmaps;
using Basil.Domain.Cryptography;
using Basil.Domain.Mechanics;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed record BeatmapReference(Md5 Hash, int Id, string Name, GameMode Mode, BeatmapRecord? Known);