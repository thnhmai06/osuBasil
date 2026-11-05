namespace Basil.Gateway;

/// <summary>How much of the allowance of a policy an authenticated caller gets.</summary>
public enum AdmissionTier
{
	/// <summary>The ordinary allowance.</summary>
	Standard,

	/// <summary>A higher allowance, for callers the host knows to be trusted.</summary>
	Elevated
}

/// <summary>The opaque identity of an authenticated caller as the second admission tier sees it.</summary>
/// <param name="Key">Identifies the caller; every request with the same key shares one allowance.</param>
/// <param name="Tier">The allowance class of the caller.</param>
public readonly record struct AdmissionSubject(string Key, AdmissionTier Tier);