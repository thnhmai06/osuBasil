using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Basil.Application.Shared.Settings;
using Basil.Domain.Users;
using SessionType = Basil.Domain.Sessions.SessionType;
using Basil.Gateway.Limits;
using Basil.Gateway.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Basil.Gateway.Admission;

/// <summary>
///     Decides whether a request enters: the server's capacity for its kind of work, and, for a signed-in user, the
///     user's allowance for that kind. A caller names the user's <see cref="Session"/>; the guard picks the allowance.
/// </summary>
public sealed class Admission(RateLimiter rate, Func<GatewayLimits> source, SessionRegistry sessions,
	ISettingsRepository settings, TimeProvider clock, LoadSignal? load = null)
{
	/// <summary>The counter of refused requests, tagged with the kind of work and the reason.</summary>
	public const string DeniedName = "basil.admission.denied";

	private static readonly Counter<long> Denied = new Meter("Basil.Gateway", "1.0")
		.CreateCounter<long>(DeniedName, description: "Requests the gateway refused, by kind of work and reason.");

	private static readonly WorkClass[] MustServe = [WorkClass.Interactive, WorkClass.Upload, WorkClass.Live, WorkClass.Poll];
	private static readonly TimeSpan UploadByteWindow = TimeSpan.FromHours(1);
	private static readonly TimeSpan GcCollectInterval = TimeSpan.FromSeconds(30);
	private const int UploadByteUnit = 1024;
	private readonly LoadSignal _load = load ?? new LoadSignal();
	private readonly int[] _running = new int[Enum.GetValues<WorkClass>().Length];
	private readonly HashSet<string> _uploaders = [];
	private readonly Lock _uploadersLock = new();
	private readonly ConcurrentDictionary<string, int> _streams = new();
	private Func<GatewayLimits> _source = source;

	public AdmissionHealth Health => Volatile.Read(ref _source)().HealthAt(CurrentLoad(Volatile.Read(ref _source)()));

	public void Apply(GatewayLimits limits)
	{
		Volatile.Write(ref _source, () => limits);
	}

	/// <summary>
	/// Admits a request. A request without a session meets only the server's capacity; a signed-in user also meets the
	/// allowance for the kind of work, and an upload is one at a time per user and counts its declared bytes against an
	/// hourly allowance. A user with the Tournament privilege has the elevated allowance, decided on each request.
	/// </summary>
	public AdmissionDecision Admit(Session? session, WorkClass workClass = WorkClass.Interactive, long uploadBytes = 0)
	{
		return Admit(SubjectOf(session), workClass, uploadBytes);
	}

	/// <summary>
	/// Admits a request of the HTTP API: a signed-in user's change (any method but GET, HEAD and OPTIONS) also meets the
	/// allowance for changes.
	/// </summary>
	public AdmissionDecision AdmitRest(HttpRequest request, Session? session, WorkClass workClass)
	{
		if (session is not null && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) &&
		    !HttpMethods.IsOptions(request.Method) && Admit(RateRule.RestMutation, session) is { Allowed: false } refused)
			return refused;

		return Admit(session, workClass, request.ContentLength ?? 0);
	}

	/// <summary>
	/// Admits a request of the osu! client, whose session is the one its token names. The poll of an open client is its
	/// own kind of work, counted apart at a higher rate, so a multiplayer round cannot lock it out; the sign-in (no or
	/// unknown token) and the osu-web endpoints keep the rate of <paramref name="workClass"/>.
	/// </summary>
	public StableAdmitted AdmitStable(string token, bool banchoPost, WorkClass workClass, long uploadBytes)
	{
		var session = token.Length > 0 ? sessions.Find(token) : null;
		if (banchoPost && session is { IsOpen: true, Type: SessionType.Stable or SessionType.StableTourney })
			workClass = WorkClass.Poll;

		return new StableAdmitted(Admit(session, workClass, uploadBytes), workClass, session);
	}

	/// <summary>Takes one event of a named rule, charged to the session's user, or to the whole server for a rule that has no user.</summary>
	public AdmissionDecision Admit(RateRule rule, Session? session = null)
	{
		var limits = settings.Current.RateLimits;
		var (subject, count, window) = rule switch
		{
			RateRule.Chat => (UserOf(session!), limits.Chat.Count, limits.Chat.Window),
			RateRule.RestMutation => (UserOf(session!), limits.Rest.Mutations.Count, limits.Rest.Mutations.Window),
			RateRule.Registration => ("server", limits.Registration.PerServer.Count, limits.Registration.PerServer.Window),
			RateRule.GcCollect => ("server", 1, GcCollectInterval),
			_ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null)
		};
		return rate.TryAcquire(rule, subject, count, window)
			? AdmissionDecision.Allow
			: Deny(rule.ToString(), AdmissionDenial.RateExceeded, window);
	}

	/// <summary>Tells whether an open osu! client is at the address; the osu-web uploads of the client are taken in only then.</summary>
	public bool HasOpenClient(IPAddress address)
	{
		// shortcut: scans the open clients; add an index by address if the online count grows large.
		return sessions.OpenStableClients().Any(client => client.State.Record.Value.Fingerprint.Ip.Equals(address));
	}

	/// <summary>Counts an open live stream of the user against the allowance of open streams; false when the user has used it up.</summary>
	public bool TryOpenStream(Session session)
	{
		var owner = UserOf(session);
		var limit = settings.Current.RateLimits.Connections.StreamsPerUser;
		if (_streams.AddOrUpdate(owner, 1, (_, count) => count + 1) <= limit) return true;

		CloseStream(session);
		Denied.Add(1, new KeyValuePair<string, object?>("class", WorkClass.Live.ToString()),
			new KeyValuePair<string, object?>("reason", "TooManyStreams"));
		return false;
	}

	/// <summary>Ends a stream that <see cref="TryOpenStream"/> counted.</summary>
	public void CloseStream(Session session)
	{
		var owner = UserOf(session);
		var left = _streams.AddOrUpdate(owner, 0, (_, count) => count - 1);
		if (left <= 0) _streams.TryRemove(new KeyValuePair<string, int>(owner, left));
	}

	private AdmissionDecision Admit(AdmissionSubject? subject, WorkClass workClass,
		long uploadBytes)
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
		return Deny(workClass.ToString(), reason, retryAfter);
	}

	private static AdmissionDecision Deny(string kind, AdmissionDenial reason, TimeSpan? retryAfter)
	{
		Denied.Add(1, new KeyValuePair<string, object?>("class", kind),
			new KeyValuePair<string, object?>("reason", reason.ToString()));
		return AdmissionDecision.Deny(reason, retryAfter);
	}

	/// <summary>Ends a request that <see cref="Admit(Session?, WorkClass, long)"/> allowed; give the same session.</summary>
	public void Release(WorkClass workClass, Session? session = null)
	{
		Release(workClass, SubjectOf(session));
	}

	private void Release(WorkClass workClass, AdmissionSubject? subject)
	{
		Interlocked.Decrement(ref _running[(int)workClass]);
		if (workClass != WorkClass.Upload || subject is not { } user) return;

		lock (_uploadersLock)
		{
			_uploaders.Remove(user.Key);
		}
	}

	private AdmissionSubject? SubjectOf(Session? session)
	{
		if (session is null) return null;

		var tier = session.EffectivePrivileges(clock.GetUtcNow()).Allows(UserPrivileges.Tournament)
			? AdmissionTier.Elevated
			: AdmissionTier.Standard;
		return new AdmissionSubject($"user:{session.Record.Id}", tier);
	}

	private static string UserOf(Session session)
	{
		return session.Record.Id.ToString(CultureInfo.InvariantCulture);
	}

	private double CurrentLoad(GatewayLimits limits)
	{
		var used = MustServe
			.Select(workClass => (double)Volatile.Read(ref _running[(int)workClass]) / limits.ConcurrencyOf(workClass))
			.Prepend(0.0).Max();

		return Math.Max(_load.Value, Math.Min(used, 1));
	}
}
