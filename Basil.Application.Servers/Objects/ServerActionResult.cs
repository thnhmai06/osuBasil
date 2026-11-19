namespace Basil.Application.Servers.Objects;

public enum ServerActionResult : byte
{
	Denied = 0,

	Ok = 1,

	NameTaken,

	Locked,

	NotFound
}