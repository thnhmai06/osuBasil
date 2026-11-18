using System.Drawing;

namespace Basil.Domain.Client;

public readonly record struct ReplayFrame(KeyState Keys, TaikoDrumState Drum, PointF Position, TimeSpan Time);

[Flags]
public enum KeyState : byte
{
	None = 0,

	Left1 = 1 << 0,

	Right1 = 1 << 1,

	Left2 = 1 << 2,

	Right2 = 1 << 3,

	Smoke = 1 << 4
}

[Flags]
public enum TaikoDrumState : byte
{
	None = 0,

	Don = 1 << 0,

	Kat = 1 << 1,

	Right = 1 << 2,

	Big = 1 << 3,

	LargeDouble = 1 << 4
}