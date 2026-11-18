namespace Basil.Protocol.Stable.Models.Users;

public sealed record ChangeAction(byte Action, string InfoText, string MapMd5, uint Mods, byte Mode, int MapId);