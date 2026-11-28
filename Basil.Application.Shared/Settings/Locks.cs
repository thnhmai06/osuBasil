namespace Basil.Application.Shared.Settings;

[Flags]
public enum Locks : byte
{
	None = 0,

	Accounts = 1 << 0,

	Rooms = 1 << 1,

	Beatmapsets = 1 << 2,

	Profiles = 1 << 3
}