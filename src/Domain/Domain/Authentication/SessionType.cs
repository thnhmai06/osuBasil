namespace Basil.Domain.Authentication;

public enum SessionType : byte
{
	Stable = 1 << 0,

	StableTourney = 1 << 1,

	Irc = 1 << 2,

	Api = 1 << 3
}