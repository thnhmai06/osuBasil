namespace Basil.Domain.Client;

/// <summary>The detection flags an osu! client reports with a score or a client check; each set bit is one sign the client noticed.</summary>
[Flags]
public enum ClientFlags : uint
{
	#region Legacy

	/// <summary>The client noticed nothing.</summary>
	Clean = 0,

	/// <summary>The client detected a speed hack.</summary>
	SpeedHackDetected = 1 << 1,

	/// <summary>The mod value of a play does not match the mods the client had; not a sign of cheating.</summary>
	IncorrectModValue = 1 << 2,

	/// <summary>More than one osu! client was running.</summary>
	MultipleOsuClients = 1 << 3,

	/// <summary>A checksum of the client failed.</summary>
	ChecksumFailure = 1 << 4,

	/// <summary>The checksum of the Flashlight mod was incorrect.</summary>
	FlashlightChecksumIncorrect = 1 << 5,

	/// <summary>The checksum of the osu! executable was incorrect.</summary>
	OsuExecutableChecksum = 1 << 6,

	/// <summary>Expected processes were missing from the client's process list.</summary>
	MissingProcessesInList = 1 << 7,

	/// <summary>The Flashlight image was tampered with.</summary>
	FlashlightImageHack = 1 << 8,

	/// <summary>A spinner hack was detected.</summary>
	SpinnerHack = 1 << 9,

	/// <summary>The client window was transparent.</summary>
	TransparentWindow = 1 << 10,

	/// <summary>Keys were pressed faster than a human can press them.</summary>
	FastPress = 1 << 11,

	/// <summary>Raw mouse input disagreed with the cursor the client saw.</summary>
	RawMouseDiscrepancy = 1 << 12,

	/// <summary>Raw keyboard input disagreed with the keys the client saw.</summary>
	RawKeyboardDiscrepancy = 1 << 13,

	#endregion

	#region LastFmFlags

	/// <summary>The client was started with the loader flag set.</summary>
	RunWithLdFlag = 1 << 14,

	/// <summary>A console window was open next to the client.</summary>
	ConsoleOpen = 1 << 15,

	/// <summary>The client had more threads than expected.</summary>
	ExtraThreads = 1 << 16,

	/// <summary>An assembly flagged as a cheat was loaded into the client.</summary>
	HqAssembly = 1 << 17,

	/// <summary>A file flagged as a cheat was found by the client.</summary>
	HqFile = 1 << 18,

	/// <summary>Registry entries flagged as a cheat were found by the client.</summary>
	RegistryEdits = 1 << 19,

	/// <summary>An SDL2 library was loaded into the client.</summary>
	Sdl2Library = 1 << 20,

	/// <summary>An OpenSSL library was loaded into the client.</summary>
	OpenSslLibrary = 1 << 21,

	/// <summary>The AQN menu sample was found by the client.</summary>
	AqnMenuSample = 1 << 22,

	#endregion
}

/// <summary>Helpers for reading <see cref="ClientFlags" />.</summary>
public static class ClientFlagsExtensions
{
	extension(ClientFlags flags)
	{
		/// <summary>Gets the names of the flags that are set, in declaration order, without <see cref="ClientFlags.Clean" />.</summary>
		/// <returns>One name per set flag; empty when none is set.</returns>
		public IReadOnlyList<string> SignNames()
		{
			return
			[
				.. Enum.GetValues<ClientFlags>()
					.Where(sign => sign != ClientFlags.Clean && flags.HasFlag(sign))
					.Select(sign => sign.ToString())
			];
		}
	}
}

/// <summary>Named groups of <see cref="ClientFlags" />.</summary>
public static class ClientFlagsGroups
{
	/// <summary>The flags that point to cheating, as opposed to those that only describe the client's environment.</summary>
	public const ClientFlags CheatSigns = ClientFlags.SpeedHackDetected |
	                                   ClientFlags.MultipleOsuClients | ClientFlags.ChecksumFailure |
	                                   ClientFlags.FlashlightChecksumIncorrect | ClientFlags.OsuExecutableChecksum |
	                                   ClientFlags.MissingProcessesInList | ClientFlags.FlashlightImageHack |
	                                   ClientFlags.SpinnerHack | ClientFlags.TransparentWindow |
	                                   ClientFlags.FastPress | ClientFlags.RawMouseDiscrepancy |
	                                   ClientFlags.RawKeyboardDiscrepancy | ClientFlags.HqAssembly |
	                                   ClientFlags.HqFile | ClientFlags.RegistryEdits;
}