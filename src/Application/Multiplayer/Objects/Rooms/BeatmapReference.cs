using Basil.Domain.Beatmaps;

using Basil.Domain.Game.Mechanics;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Application.Multiplayer.Objects.Rooms;

public sealed record BeatmapReference(Md5Hash Hash, int Id, string Name, GameMode Mode, BeatmapRecord? Known);