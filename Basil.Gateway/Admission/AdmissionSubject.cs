namespace Basil.Gateway.Admission;

public enum AdmissionTier : byte
{
	Standard,

	Elevated
}

public readonly record struct AdmissionSubject(string Key, AdmissionTier Tier);