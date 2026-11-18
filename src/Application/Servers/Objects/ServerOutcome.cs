namespace Basil.Application.Servers.Objects;

public enum ServerOutcome : byte
{
	Denied = 0,

	Ok = 1,

	NameTaken,

	TooLarge,

	InvalidImage,

	DimensionsTooLarge,

	Locked,

	NotFound
}