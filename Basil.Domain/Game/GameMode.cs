namespace Basil.Domain.Game;

/// <summary>The ruleset a beatmap is played in; the values carry the game numbering.</summary>
public enum GameMode : byte
{
	/// <summary>The osu! ruleset: click circles, follow sliders and spin spinners.</summary>
	Standard = 0,

	/// <summary>The osu!taiko ruleset: hit drum notes with the center and rim of a drum.</summary>
	Taiko = 1,

	/// <summary>The osu!catch ruleset: move a catcher to collect falling fruit.</summary>
	Catch = 2,

	/// <summary>The osu!mania ruleset: hit notes falling down a number of key columns.</summary>
	Mania = 3
}