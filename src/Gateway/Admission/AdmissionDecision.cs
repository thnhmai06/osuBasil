namespace Basil.Gateway.Admission;

/// <summary>Why a request was refused.</summary>
public enum AdmissionDenial : byte
{
	/// <summary>The caller made more requests in the window than its tier allows.</summary>
	RateExceeded = 1,

	/// <summary>The kind of work the request asks for is already running at its concurrency cap.</summary>
	ClassSaturated = 2,

	/// <summary>The server is overloaded and refuses this kind of work.</summary>
	Shed = 3
}

/// <summary>The answer of the gateway: let the request in, or refuse it.</summary>
/// <param name="Allowed">Whether the request may proceed.</param>
/// <param name="Reason">Why it was refused; <see langword="null" /> when allowed.</param>
/// <param name="RetryAfter">How long the caller should wait before trying again, when a delay is known.</param>
public readonly record struct AdmissionDecision(
	bool Allowed,
	AdmissionDenial? Reason = null,
	TimeSpan? RetryAfter = null)
{
	/// <summary>Gets the decision to let the request proceed.</summary>
	public static AdmissionDecision Allow { get; } = new(true);

	/// <summary>Refuses a request.</summary>
	/// <param name="reason">Why it was refused.</param>
	/// <param name="retryAfter">How long to wait before trying again.</param>
	/// <returns>A denial.</returns>
	public static AdmissionDecision Deny(AdmissionDenial reason, TimeSpan? retryAfter = null)
	{
		return new AdmissionDecision(false, reason, retryAfter);
	}
}