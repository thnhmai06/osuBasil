using Basil.Gateway.Limits;
using Basil.Gateway.RateLimiting;

namespace Basil.Gateway.Admission;

public sealed class Admission(TimeProvider clock, Func<GatewayLimits> source, LoadSignal? load = null)
{
	private static readonly WorkClass[] MustServe = [WorkClass.Interactive, WorkClass.Upload, WorkClass.Live, WorkClass.Poll];
	private readonly LoadSignal _load = load ?? new LoadSignal();
	private readonly RateLimiter _rate = new(clock);
	private readonly int[] _running = new int[Enum.GetValues<WorkClass>().Length];
	private Func<GatewayLimits> _source = source;

	public Admission(TimeProvider clock, GatewayLimits? limits = null, LoadSignal? load = null)
		: this(clock, () => limits ?? new GatewayLimits(), load)
	{
	}

	public AdmissionHealth Health => Volatile.Read(ref _source)().HealthAt(CurrentLoad(Volatile.Read(ref _source)()));

	public void Apply(GatewayLimits limits)
	{
		Volatile.Write(ref _source, () => limits);
	}

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

		var (key, allowance) = workClass switch
		{
			WorkClass.Asset => ($"asset:{subject.Key}", current.AssetRequests),
			WorkClass.Poll => ($"poll:{subject.Key}", current.PollRequests),
			_ => (subject.Key, current.AllowanceOf(subject.Tier))
		};
		if (_rate.TryAcquire(key, allowance, current.Window))
			return AdmissionDecision.Allow;
		Release(workClass);
		return AdmissionDecision.Deny(AdmissionDenial.RateExceeded, current.Window);
	}

	public void Release(WorkClass workClass)
	{
		Interlocked.Decrement(ref _running[(int)workClass]);
	}

	private double CurrentLoad(GatewayLimits limits)
	{
		var used = MustServe
			.Select(workClass => (double)Volatile.Read(ref _running[(int)workClass]) / limits.ConcurrencyOf(workClass))
			.Prepend(0.0).Max();

		return Math.Max(_load.Value, Math.Min(used, 1));
	}
}