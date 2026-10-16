namespace Basil.Domain.Bancho;

/// <summary>The combinations of <see cref="BanchoFlags" /> the server reasons about.</summary>
public static class BanchoFlagsGroups
{
	/// <summary>The flags that indicate tampering with the client or its environment.</summary>
	public const BanchoFlags CheatSigns = BanchoFlags.SpeedHackDetected | BanchoFlags.IncorrectModValue |
	                                      BanchoFlags.MultipleOsuClients | BanchoFlags.ChecksumFailure |
	                                      BanchoFlags.FlashlightChecksumIncorrect | BanchoFlags.OsuExecutableChecksum |
	                                      BanchoFlags.MissingProcessesInList | BanchoFlags.FlashlightImageHack |
	                                      BanchoFlags.SpinnerHack | BanchoFlags.TransparentWindow |
	                                      BanchoFlags.FastPress | BanchoFlags.RawMouseDiscrepancy |
	                                      BanchoFlags.RawKeyboardDiscrepancy | BanchoFlags.HqAssembly |
	                                      BanchoFlags.HqFile | BanchoFlags.RegistryEdits;
}
