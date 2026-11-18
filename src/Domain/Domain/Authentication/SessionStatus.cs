namespace Basil.Domain.Authentication;

public enum SessionStatus : byte
{
	Active,

	Replaceable,

	Expired,

	Ended
}
