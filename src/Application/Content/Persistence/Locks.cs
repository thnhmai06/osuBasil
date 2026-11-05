namespace Basil.Application.Content.Persistence;

/// <summary>The server-wide creation and modification areas that can be restricted to their managers.</summary>
[Flags]
public enum Locks : byte
{
	/// <summary>No area is locked.</summary>
	None = 0,

	/// <summary>Account registration is restricted to account managers.</summary>
	Accounts = 1 << 0,

	/// <summary>Room creation is restricted to room managers.</summary>
	Rooms = 1 << 1,

	/// <summary>Beatmapset upload and deletion are restricted to beatmapset managers.</summary>
	Beatmapsets = 1 << 2,

	/// <summary>Profile and password changes are restricted to account managers.</summary>
	Profiles = 1 << 3
}