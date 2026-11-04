namespace Basil.Gateway;

/// <summary>The second admission tier: decides, per authenticated subject, whether a request may enter.</summary>
public sealed class Admission(TimeProvider clock, GatewayLimits? limits = null)
{
	private readonly RateLimiter _rate = new(clock);
	private GatewayLimits _limits = limits ?? new GatewayLimits();

	/// <summary>Replaces the limits; requests decided afterwards use the new ones.</summary>
	/// <param name="limits">The new limits.</param>
	public void Apply(GatewayLimits limits)
	{
		Volatile.Write(ref _limits, limits);
	}

	/// <summary>Counts a request of the subject and decides whether it may proceed.</summary>
	/// <param name="subject">Who is asking.</param>
	/// <returns>Allow, or a denial whose retry delay is at most one window.</returns>
	public AdmissionDecision Admit(AdmissionSubject subject)
	{
		var current = Volatile.Read(ref _limits);
		return _rate.TryAcquire(subject.Key, current.AllowanceOf(subject.Tier), current.Window)
			? AdmissionDecision.Allow
			: AdmissionDecision.Deny(AdmissionDenial.RateExceeded, current.Window);
	}
}
