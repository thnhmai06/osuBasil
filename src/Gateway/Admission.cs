namespace Basil.Gateway;

/// <summary>The second admission tier: decides, per authenticated subject and kind of work, whether a request may enter.</summary>
/// <param name="clock">The clock the rate window is measured with.</param>
/// <param name="limits">The starting limits; the defaults when omitted.</param>
/// <param name="load">The load of the server; an idle server when omitted.</param>
public sealed class Admission(TimeProvider clock, GatewayLimits? limits = null, LoadSignal? load = null)
{
	private readonly LoadSignal _load = load ?? new LoadSignal();
	private readonly RateLimiter _rate = new(clock);
	private readonly int[] _running = new int[Enum.GetValues<WorkClass>().Length];
	private GatewayLimits _limits = limits ?? new GatewayLimits();

	/// <summary>Gets how the server is doing, from its load.</summary>
	public AdmissionHealth Health => Volatile.Read(ref _limits).HealthAt(_load.Value);

	/// <summary>Replaces the limits; requests decided afterwards use the new ones.</summary>
	/// <param name="limits">The new limits.</param>
	public void Apply(GatewayLimits limits)
	{
		Volatile.Write(ref _limits, limits);
	}

	/// <summary>
	///     Decides whether a request may proceed: it is refused when the server sheds its kind of work, when that kind
	///     is at its cap, or when the subject is over its allowance.
	/// </summary>
	/// <param name="subject">Who is asking.</param>
	/// <param name="workClass">The kind of work the request asks for.</param>
	/// <returns>
	///     Allow, or a denial with a retry delay. Every Allow must be matched by one <see cref="Release" /> when the
	///     request ends.
	/// </returns>
	public AdmissionDecision Admit(AdmissionSubject subject, WorkClass workClass = WorkClass.Interactive)
	{
		var current = Volatile.Read(ref _limits);
		if (current.Sheds(workClass, _load.Value))
			return AdmissionDecision.Deny(AdmissionDenial.Shed, current.BusyRetryAfter);

		if (Interlocked.Increment(ref _running[(int)workClass]) > current.ConcurrencyOf(workClass))
		{
			Release(workClass);
			return AdmissionDecision.Deny(AdmissionDenial.ClassSaturated, current.BusyRetryAfter);
		}

		if (_rate.TryAcquire(subject.Key, current.AllowanceOf(subject.Tier), current.Window))
			return AdmissionDecision.Allow;
		Release(workClass);
		return AdmissionDecision.Deny(AdmissionDenial.RateExceeded, current.Window);
	}

	/// <summary>Gives back the place an allowed request held in its kind of work.</summary>
	/// <param name="workClass">The kind of work the request was admitted as.</param>
	public void Release(WorkClass workClass)
	{
		Interlocked.Decrement(ref _running[(int)workClass]);
	}
}
