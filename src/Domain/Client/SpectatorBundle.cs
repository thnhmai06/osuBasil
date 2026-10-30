namespace Basil.Domain.Client;

/// <summary>The frames a player sent in one packet while being spectated.</summary>
/// <param name="Action">What the player is doing.</param>
/// <param name="Frames">The cursor and button samples, in time order.</param>
/// <param name="Score">The running score at the end of the bundle.</param>
/// <param name="Sequence">The position of the bundle in the stream the client sent.</param>
public sealed record SpectatorBundle(
	SpectatorAction Action,
	IReadOnlyList<SpectatorFrame> Frames,
	SpectatorScore Score,
	int Sequence);

/// <summary>What the player being spectated is doing.</summary>
public enum SpectatorAction : byte
{
	/// <summary>The player is playing.</summary>
	Standard = 0,

	/// <summary>The player started a new beatmap.</summary>
	NewSong = 1,

	/// <summary>The player skipped the intro.</summary>
	Skip = 2,

	/// <summary>The player completed the beatmap.</summary>
	Completion = 3,

	/// <summary>The player failed the beatmap.</summary>
	Fail = 4,

	/// <summary>The player paused.</summary>
	Pause = 5,

	/// <summary>The player resumed.</summary>
	Unpause = 6,

	/// <summary>The player went to song selection.</summary>
	SongSelect = 7,

	/// <summary>The player is spectating someone else.</summary>
	WatchingOther = 8
}

// TODO: Sử dụng dạng Object/Struct hóa có sẵn, chứ không flatten dữ liệu ở đây (2 below struct).
// Ở Protocol sẽ lo liệu object truyền đi se ntn, ở đây Domain chỉ quan tâm nghiệp vụ.

/// <summary>One cursor and button sample of a play.</summary>
/// <param name="ButtonState">The osu! button bits pressed.</param>
/// <param name="TaikoByte">The osu!taiko hit bits.</param>
/// <param name="X">The cursor's horizontal position.</param>
/// <param name="Y">The cursor's vertical position.</param>
/// <param name="Time">The time into the beatmap, in milliseconds.</param>
public readonly record struct SpectatorFrame(byte ButtonState, byte TaikoByte, float X, float Y, int Time);

/// <summary>The running score of a play at a moment.</summary>
/// <param name="Time">The time into the beatmap, in milliseconds.</param>
/// <param name="Id">The slot of the player.</param>
/// <param name="Num300">The number of 300 hits.</param>
/// <param name="Num100">The number of 100 hits.</param>
/// <param name="Num50">The number of 50 hits.</param>
/// <param name="NumGeki">The number of geki hits.</param>
/// <param name="NumKatu">The number of katu hits.</param>
/// <param name="NumMiss">The number of misses.</param>
/// <param name="TotalScore">The score so far.</param>
/// <param name="MaxCombo">The highest combo so far.</param>
/// <param name="CurrentCombo">The current combo.</param>
/// <param name="Perfect">Whether the play is perfect so far.</param>
/// <param name="CurrentHp">The player's health, from 0 to 254.</param>
public readonly record struct SpectatorScore(int Time, int Id, int Num300, int Num100, int Num50, int NumGeki,
	int NumKatu, int NumMiss, int TotalScore, int MaxCombo, int CurrentCombo, bool Perfect, int CurrentHp);
