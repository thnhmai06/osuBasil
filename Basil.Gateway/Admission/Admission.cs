using System.Diagnostics.Metrics;
using Basil.Gateway.Limits;
using Basil.Gateway.RateLimiting;

namespace Basil.Gateway.Admission;

/// <summary>Decides whether a request enters: the server's capacity for its kind of work, and, for a signed-in user, the user's allowance for that kind.</summary>
public sealed class Admission(RateLimiter rate, Func<GatewayLimits> source, LoadSignal? load = null)
{
	/// <summary>The counter of refused requests, tagged with the kind of work and the reason.</summary>
	public const string DeniedName = "basil.admission.denied";

	private static readonly Counter<long> Denied = new Meter("Basil.Gateway", "1.0")
		.CreateCounter<long>(DeniedName, description: "Requests the gateway refused, by kind of work and reason.");

	private static readonly WorkClass[] MustServe = [WorkClass.Interactive, WorkClass.Upload, WorkClass.Live, WorkClass.Poll];
	private static readonly TimeSpan UploadByteWindow = TimeSpan.FromHours(1);
	private const int UploadByteUnit = 1024;
	private readonly LoadSignal _load = load ?? new LoadSignal();
	private readonly int[] _running = new int[Enum.GetValues<WorkClass>().Length];
	private readonly HashSet<string> _uploaders = [];
	private readonly Lock _uploadersLock = new();
	private Func<GatewayLimits> _source = source;

	public Admission(RateLimiter rate, GatewayLimits? limits = null, LoadSignal? load = null)
		: this(rate, () => limits ?? new GatewayLimits(), load)
	{
	}

	public AdmissionHealth Health => Volatile.Read(ref _source)().HealthAt(CurrentLoad(Volatile.Read(ref _source)()));

	public void Apply(GatewayLimits limits)
	{
		Volatile.Write(ref _source, () => limits);
	}

	/// <summary>
	/// Admits a request. A request without a user meets only the server's capacity; a user also meets the allowance for the
	/// kind of work, and an upload is one at a time per user and counts its declared bytes against an hourly allowance.
	/// </summary>
	public AdmissionDecision Admit(AdmissionSubject? subject, WorkClass workClass = WorkClass.Interactive,
		long uploadBytes = 0)
	{
		var current = Volatile.Read(ref _source)();
		if (current.Sheds(workClass, MustServe.Contains(workClass) ? _load.Value : CurrentLoad(current)))
			return Deny(workClass, AdmissionDenial.Shed, current.BusyRetryAfter);

		if (Interlocked.Increment(ref _running[(int)workClass]) > current.ConcurrencyOf(workClass))
		{
			Release(workClass);
			return Deny(workClass, AdmissionDenial.ClassSaturated, current.BusyRetryAfter);
		}

		if (subject is not { } user) return AdmissionDecision.Allow;

		if (!rate.TryAcquire($"{workClass}:{user.Key}", current.AllowanceOf(workClass, user.Tier), current.Window))
		{
			Release(workClass);
			return Deny(workClass, AdmissionDenial.RateExceeded, current.Window);
		}

		if (workClass != WorkClass.Upload) return AdmissionDecision.Allow;

		var units = (int)Math.Min(int.MaxValue, (uploadBytes + UploadByteUnit - 1) / UploadByteUnit);
		var allowed = (int)Math.Min(int.MaxValue, current.UploadBytesPerHour / UploadByteUnit);
		lock (_uploadersLock)
		{
			if (!_uploaders.Add(user.Key))
			{
				Release(workClass);
				return Deny(workClass, AdmissionDenial.RateExceeded, current.BusyRetryAfter);
			}
		}

		if (units == 0 || rate.TryAcquire($"upload-bytes:{user.Key}", allowed, UploadByteWindow, units))
			return AdmissionDecision.Allow;

		Release(workClass, user);
		return Deny(workClass, AdmissionDenial.RateExceeded, UploadByteWindow);
	}

	private static AdmissionDecision Deny(WorkClass workClass, AdmissionDenial reason, TimeSpan? retryAfter)
	{
		Denied.Add(1, new KeyValuePair<string, object?>("class", workClass.ToString()),
			new KeyValuePair<string, object?>("reason", reason.ToString()));
		return AdmissionDecision.Deny(reason, retryAfter);
	}

	/// <summary>Ends a request that <see cref="Admit"/> allowed; give the same subject.</summary>
	public void Release(WorkClass workClass, AdmissionSubject? subject = null)
	{
		Interlocked.Decrement(ref _running[(int)workClass]);
		if (workClass != WorkClass.Upload || subject is not { } user) return;

		lock (_uploadersLock)
		{
			_uploaders.Remove(user.Key);
		}
	}

	private double CurrentLoad(GatewayLimits limits)
	{
		var used = MustServe
			.Select(workClass => (double)Volatile.Read(ref _running[(int)workClass]) / limits.ConcurrencyOf(workClass))
			.Prepend(0.0).Max();

		return Math.Max(_load.Value, Math.Min(used, 1));
	}
}
