namespace Basil.Application.Beatmaps.Objects;

/// <summary>What happened to a ruleset operation that was not refused for lack of permissions.</summary>
public enum RulesetOutcome : byte
{
	/// <summary>The operation succeeded.</summary>
	Ok = 1,

	/// <summary>A beatmap reload is already running.</summary>
	Busy
}
