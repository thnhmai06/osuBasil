namespace Basil.Protocol.Stable.Models.Wire;

/// <summary>The mod bits the osu! client uses.</summary>
[Flags]
public enum WireMods : uint
{
	/// <summary>No mod.</summary>
	NoMod = 0,

	/// <summary>The NoFail mod.</summary>
	NoFail = 1 << 0,

	/// <summary>The Easy mod.</summary>
	Easy = 1 << 1,

	/// <summary>The TouchScreen mod.</summary>
	TouchScreen = 1 << 2,

	/// <summary>The Hidden mod.</summary>
	Hidden = 1 << 3,

	/// <summary>The HardRock mod.</summary>
	HardRock = 1 << 4,

	/// <summary>The SuddenDeath mod.</summary>
	SuddenDeath = 1 << 5,

	/// <summary>The DoubleTime mod.</summary>
	DoubleTime = 1 << 6,

	/// <summary>The Relax mod.</summary>
	Relax = 1 << 7,

	/// <summary>The HalfTime mod.</summary>
	HalfTime = 1 << 8,

	/// <summary>The Nightcore mod.</summary>
	Nightcore = 1 << 9,

	/// <summary>The Flashlight mod.</summary>
	Flashlight = 1 << 10,

	/// <summary>The Autoplay mod.</summary>
	Autoplay = 1 << 11,

	/// <summary>The SpunOut mod.</summary>
	SpunOut = 1 << 12,

	/// <summary>The Autopilot mod.</summary>
	Autopilot = 1 << 13,

	/// <summary>The Perfect mod.</summary>
	Perfect = 1 << 14,

	/// <summary>The 4K mod.</summary>
	Key4 = 1 << 15,

	/// <summary>The 5K mod.</summary>
	Key5 = 1 << 16,

	/// <summary>The 6K mod.</summary>
	Key6 = 1 << 17,

	/// <summary>The 7K mod.</summary>
	Key7 = 1 << 18,

	/// <summary>The 8K mod.</summary>
	Key8 = 1 << 19,

	/// <summary>The FadeIn mod.</summary>
	FadeIn = 1 << 20,

	/// <summary>The Random mod.</summary>
	Random = 1 << 21,

	/// <summary>The Cinema mod.</summary>
	Cinema = 1 << 22,

	/// <summary>The Target mod.</summary>
	Target = 1 << 23,

	/// <summary>The 9K mod.</summary>
	Key9 = 1 << 24,

	/// <summary>The KeyCoop mod.</summary>
	KeyCoop = 1 << 25,

	/// <summary>The 1K mod.</summary>
	Key1 = 1 << 26,

	/// <summary>The 3K mod.</summary>
	Key3 = 1 << 27,

	/// <summary>The 2K mod.</summary>
	Key2 = 1 << 28,

	/// <summary>The ScoreV2 mod.</summary>
	ScoreV2 = 1 << 29,

	/// <summary>The Mirror mod.</summary>
	Mirror = 1 << 30
}