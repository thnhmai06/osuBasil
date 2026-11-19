namespace Basil.Gateway.Admission;

public enum AdmissionDenial : byte
{
	RateExceeded = 1,

	ClassSaturated = 2,

	Shed = 3
}

public readonly record struct AdmissionDecision(
	bool Allowed,
	AdmissionDenial? Reason = null,
	TimeSpan? RetryAfter = null)
{
	public static AdmissionDecision Allow { get; } = new(true);

	public static AdmissionDecision Deny(AdmissionDenial reason, TimeSpan? retryAfter = null)
	{
		return new AdmissionDecision(false, reason, retryAfter);
	}
}