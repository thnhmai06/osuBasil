namespace Basil.Domain.Scores;

/// <summary>
///     Represents the anticheat flags reported by an osu! client during a score submission.
/// </summary>
/// <remarks>
///     Sent as part of the score submission payload. These flags serve as basic heuristics;
///     many are legacy checks, prone to false-positives under modern Windows environments,
///     or used solely for telemetry/data collection.
/// </remarks>
[Flags]
public enum ClientFlags : uint
{
	/// <summary>
	///     The client triggered no anticheat flags.
	/// </summary>
	Clean = 0,

	/// <summary>
	///     Indicates game time acceleration or framerate/timer manipulation (Speedhack).
	/// </summary>
	SpeedHackDetected = 1 << 1,

	/// <summary>
	///     Indicates an illegal or contradictory combination of difficulty modifiers.
	/// </summary>
	IncorrectModValue = 1 << 2,

	/// <summary>
	///     Indicates multiple instances of the osu! executable running simultaneously.
	/// </summary>
	MultipleOsuClients = 1 << 3,

	/// <summary>
	///     Indicates a checksum discrepancy in core beatmap or replay files.
	/// </summary>
	ChecksumFailure = 1 << 4,

	/// <summary>
	///     Indicates a checksum mismatch in the Flashlight mod rendering code or overlay textures.
	/// </summary>
	FlashlightChecksumIncorrect = 1 << 5,

	/// <summary>
	///     Indicates the executable hash did not match official osu! binaries (assembly tampering/patching).
	/// </summary>
	OsuExecutableChecksum = 1 << 6,

	/// <summary>
	///     Indicates expected system processes or threads were missing or hidden (anti-debugging/process hiding).
	/// </summary>
	MissingProcessesInList = 1 << 7,

	/// <summary>
	///     Indicates modification or removal of the Flashlight dim overlay texture in memory or files.
	/// </summary>
	FlashlightImageHack = 1 << 8,

	/// <summary>
	///     Indicates automatic or mathematically impossible spinning speeds (Spin hack / Auto-spinner).
	/// </summary>
	SpinnerHack = 1 << 9,

	/// <summary>
	///     Indicates detection of transparent overlay windows or external drawing hooks over the client surface.
	/// </summary>
	TransparentWindow = 1 << 10,

	/// <summary>
	///     Indicates keypress intervals or durations below normal human thresholds (Relax hack / Rapid tap).
	/// </summary>
	FastPress = 1 << 11,

	/// <summary>
	///     Indicates a discrepancy between raw hardware mouse input and in-game cursor coordinates.
	/// </summary>
	RawMouseDiscrepancy = 1 << 12,

	/// <summary>
	///     Indicates a discrepancy between raw hardware keyboard state and simulated keypress events.
	/// </summary>
	RawKeyboardDiscrepancy = 1 << 13
}

/// <summary>
///     Represents client integrity flags sent to the LastFM telemetry endpoint (<c>osu-lastfm.php</c>).
/// </summary>
/// <remarks>
///     Evaluated via <c>ClientIntegrityService</c>. Despite sharing bitwise continuity with <see cref="ClientFlags" />,
///     these flags occupy higher bit shifts (14–22) and are dispatched via background telemetry calls outside score
///     submissions.
/// </remarks>
[Flags]
public enum LastFmFlags : uint
{
	/// <summary>
	///     Indicates the client was launched with the <c>-ld</c> (Low Detail / Lag Delay) startup parameter.
	/// </summary>
	RunWithLdFlag = 1 << 14,

	/// <summary>
	///     Indicates an attached debugging or system console window was detected.
	/// </summary>
	ConsoleOpen = 1 << 15,

	/// <summary>
	///     Indicates unauthorized extra threads injected into the osu! process (common indicator of DLL injection).
	/// </summary>
	ExtraThreads = 1 << 16,

	/// <summary>
	///     Indicates an external <c>hq.dll</c> or HQ cheat assembly was loaded into the process AppDomain.
	/// </summary>
	HqAssembly = 1 << 17,

	/// <summary>
	///     Indicates an HQ cheat payload file was present in the client execution directory.
	/// </summary>
	HqFile = 1 << 18,

	/// <summary>
	///     Indicates unauthorized modifications to system registry keys associated with input hooks or process launching.
	/// </summary>
	RegistryEdits = 1 << 19,

	/// <summary>
	///     Indicates <c>SDL2.dll</c> was loaded into memory (osu!stable does not use SDL2 natively; typically used by
	///     injection wrappers).
	/// </summary>
	Sdl2Library = 1 << 20,

	/// <summary>
	///     Indicates OpenSSL libraries (<c>libeay32.dll</c>/<c>ssleay32.dll</c>) were loaded into memory.
	/// </summary>
	OpenSslLibrary = 1 << 21,

	/// <summary>
	///     Indicates memory or sound artifacts associated with the legacy AQN (Aoba Quality Network) cheat module were
	///     detected.
	/// </summary>
	AqnMenuSample = 1 << 22
}