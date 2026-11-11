using Basil.Gateway.Limits;
using Basil.Gateway.RateLimiting;

namespace Basil.Gateway.Admission;

/// <summary>The second admission tier: decides, per authenticated subject and kind of work, whether a request may enter.</summary>
/// <param name="clock">The clock the rate window is measured with.</param>
/// <param name="source">
///     Supplies the limits in force; it is asked for each decision, so a change applies to the next
///     request.
/// </param>
/// <param name="load">The load of the server; an idle server when omitted.</param>
public sealed class Admission(TimeProvider clock, Func<GatewayLimits> source, LoadSignal? load = null)
{
	private readonly LoadSignal _load = load ?? new LoadSignal();
	private static readonly WorkClass[] MustServe = [WorkClass.Interactive, WorkClass.Upload, WorkClass.Live];
	private readonly RateLimiter _rate = new(clock);
	private readonly int[] _running = new int[Enum.GetValues<WorkClass>().Length];
	private Func<GatewayLimits> _source = source;

	/// <summary>Creates the tier with fixed limits that <see cref="Apply" /> can replace.</summary>
	/// <param name="clock">The clock the rate window is measured with.</param>
	/// <param name="limits">The starting limits; the defaults when omitted.</param>
	/// <param name="load">The load of the server; an idle server when omitted.</param>
	public Admission(TimeProvider clock, GatewayLimits? limits = null, LoadSignal? load = null)
		: this(clock, () => limits ?? new GatewayLimits(), load)
	{
	}

	/// <summary>Gets how the server is doing, from its load.</summary>
	public AdmissionHealth Health => Volatile.Read(ref _source)().HealthAt(CurrentLoad(Volatile.Read(ref _source)()));

	/// <summary>Replaces the limits; requests decided afterwards use the new ones.</summary>
	/// <param name="limits">The new limits.</param>
	public void Apply(GatewayLimits limits)
	{
		Volatile.Write(ref _source, () => limits);
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
		var current = Volatile.Read(ref _source)();
		if (current.Sheds(workClass, MustServe.Contains(workClass) ? _load.Value : CurrentLoad(current)))
			return AdmissionDecision.Deny(AdmissionDenial.Shed, current.BusyRetryAfter);

		if (Interlocked.Increment(ref _running[(int)workClass]) > current.ConcurrencyOf(workClass))
		{
			Release(workClass);
			return AdmissionDecision.Deny(AdmissionDenial.ClassSaturated, current.BusyRetryAfter);
		}

		var isAsset = workClass == WorkClass.Asset;
		var key = isAsset ? $"asset:{subject.Key}" : subject.Key;
		var allowance = isAsset ? current.AssetRequests : current.AllowanceOf(subject.Tier);
		if (_rate.TryAcquire(key, allowance, current.Window))
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

	/// <summary>
	///     The load that decides health and the shedding of listings and pictures: the larger of the reported load and
	///     how full the classes that are never shed first are. Those classes themselves are held back only by their cap
	///     and by the reported load, never by their own fullness.
	/// </summary>
	private double CurrentLoad(GatewayLimits limits)
	{
		var used = MustServe
			.Select(workClass => (double)Volatile.Read(ref _running[(int)workClass]) / limits.ConcurrencyOf(workClass))
			.Prepend(0.0).Max();

		return Math.Max(_load.Value, Math.Min(used, 1));
	}
}