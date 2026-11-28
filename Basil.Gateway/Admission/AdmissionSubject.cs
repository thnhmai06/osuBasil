namespace Basil.Gateway.Admission;

public enum AdmissionTier : byte
{
	Standard,

	Elevated
}

internal readonly record struct AdmissionSubject(string Key, AdmissionTier Tier);