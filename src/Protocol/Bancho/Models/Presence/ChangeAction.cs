namespace Basil.Protocol.Bancho.Models.Presence;

/// <summary>Carries the status a client reports in the change-action packet.</summary>
/// <param name="Action">The numeric action id of the client's status.</param>
/// <param name="InfoText">The status text shown in the user card.</param>
/// <param name="MapMd5">The md5 hash of the beatmap involved, empty when none.</param>
/// <param name="Mods">The bitwise combination of mods in use.</param>
/// <param name="Mode">The game mode of the status.</param>
/// <param name="MapId">The id of the beatmap involved.</param>
public sealed record ChangeAction(byte Action, string InfoText, string MapMd5, uint Mods, byte Mode, int MapId);
