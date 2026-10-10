using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Utilities;

namespace Basil.Application.Storage.Contracts.Multiplayer.Room;

/// <summary>The beatmap a room has selected, as the client describes it; the server may not have it.</summary>
/// <param name="Hash">The MD5 of the beatmap file.</param>
/// <param name="Id">The beatmap id.</param>
/// <param name="Name">The display name of the beatmap.</param>
/// <param name="Mode">The game mode the beatmap was made for.</param>
/// <param name="Known">The beatmap as stored on the server, or <see langword="null" /> when the server does not have it.</param>
public sealed record BeatmapReference(Md5 Hash, int Id, string Name, GameMode Mode, Beatmap? Known);