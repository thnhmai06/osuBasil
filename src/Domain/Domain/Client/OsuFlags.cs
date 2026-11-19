namespace Basil.Domain.Client;

[Flags]
public enum OsuFlags : uint
{
	#region Legacy

	Clean = 0,

	SpeedHackDetected = 1 << 1,

	IncorrectModValue = 1 << 2,

	MultipleOsuClients = 1 << 3,

	ChecksumFailure = 1 << 4,

	FlashlightChecksumIncorrect = 1 << 5,

	OsuExecutableChecksum = 1 << 6,

	MissingProcessesInList = 1 << 7,

	FlashlightImageHack = 1 << 8,

	SpinnerHack = 1 << 9,

	TransparentWindow = 1 << 10,

	FastPress = 1 << 11,

	RawMouseDiscrepancy = 1 << 12,

	RawKeyboardDiscrepancy = 1 << 13,

	#endregion

	#region LastFmFlags

	RunWithLdFlag = 1 << 14,

	ConsoleOpen = 1 << 15,

	ExtraThreads = 1 << 16,

	HqAssembly = 1 << 17,

	HqFile = 1 << 18,

	RegistryEdits = 1 << 19,

	Sdl2Library = 1 << 20,

	OpenSslLibrary = 1 << 21,

	AqnMenuSample = 1 << 22,

	#endregion
}

public static class OsuFlagsExtensions
{
	extension(OsuFlags flags)
	{
		public IReadOnlyList<string> SignNames()
		{
			return
			[
				.. Enum.GetValues<OsuFlags>()
					.Where(sign => sign != OsuFlags.Clean && flags.HasFlag(sign))
					.Select(sign => sign.ToString())
			];
		}
	}
}

public static class OsuFlagsGroups
{
	public const OsuFlags CheatSigns = OsuFlags.SpeedHackDetected | OsuFlags.IncorrectModValue |
	                                   OsuFlags.MultipleOsuClients | OsuFlags.ChecksumFailure |
	                                   OsuFlags.FlashlightChecksumIncorrect | OsuFlags.OsuExecutableChecksum |
	                                   OsuFlags.MissingProcessesInList | OsuFlags.FlashlightImageHack |
	                                   OsuFlags.SpinnerHack | OsuFlags.TransparentWindow |
	                                   OsuFlags.FastPress | OsuFlags.RawMouseDiscrepancy |
	                                   OsuFlags.RawKeyboardDiscrepancy | OsuFlags.HqAssembly |
	                                   OsuFlags.HqFile | OsuFlags.RegistryEdits;
}