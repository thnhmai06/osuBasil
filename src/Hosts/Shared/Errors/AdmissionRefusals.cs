using System.Globalization;
using Basil.Application.Users.Objects.Connections;
using Basil.Gateway.Admission;
using Basil.Gateway.Limits;

namespace Basil.Host.Shared.Errors;

/// <summary>Tells the hosts who an admission request is for and how a refusal is reported, the same on every transport.</summary>
public static class AdmissionRefusals
{
	/// <summary>The error code of a refusal for too many requests.</summary>
	public const string RateLimitedCode = "rateLimited";

	/// <summary>The message of a refusal for too many requests.</summary>
	public const string RateLimitedMessage = "Too many requests.";

	/// <summary>The error code of a refusal because the server is busy.</summary>
	public const string BusyCode = "busy";

	/// <summary>The message of a refusal because the server is busy.</summary>
	public const string BusyMessage = "The server is busy. Try again later.";

	/// <summary>Names the signed-in user a connection belongs to, so all of the user's connections share one allowance.</summary>
	/// <param name="connection">The connection of the request.</param>
	/// <returns>The subject of the user.</returns>
	public static AdmissionSubject SubjectOf(Connection connection)
	{
		return new AdmissionSubject($"user:{connection.Record.Id}", AdmissionTier.Standard);
	}

	/// <summary>Names a caller that is not signed in by an address or a socket name.</summary>
	/// <param name="address">The address or socket name the caller is known by.</param>
	/// <returns>The subject of that name.</returns>
	public static AdmissionSubject SubjectOf(string address)
	{
		return new AdmissionSubject($"ip:{address}", AdmissionTier.Standard);
	}

	/// <summary>Gets the HTTP status that reports a refusal.</summary>
	/// <param name="decision">A refused decision.</param>
	/// <returns>429 when the caller sent too much, 503 when the server cannot take the work.</returns>
	public static int StatusOf(AdmissionDecision decision)
	{
		return decision.Reason == AdmissionDenial.RateExceeded ? 429 : 503;
	}

	/// <summary>Gets the error code of a refusal.</summary>
	/// <param name="decision">A refused decision.</param>
	/// <returns>The code the API and the web endpoints report.</returns>
	public static string CodeOf(AdmissionDecision decision)
	{
		return decision.Reason == AdmissionDenial.RateExceeded ? RateLimitedCode : BusyCode;
	}

	/// <summary>Gets the message of a refusal.</summary>
	/// <param name="decision">A refused decision.</param>
	/// <returns>The text the API and the web endpoints report.</returns>
	public static string MessageOf(AdmissionDecision decision)
	{
		return decision.Reason == AdmissionDenial.RateExceeded ? RateLimitedMessage : BusyMessage;
	}

	/// <summary>Gets the value of the <c>Retry-After</c> header of a refusal.</summary>
	/// <param name="decision">A refused decision.</param>
	/// <returns>The whole seconds to wait, at least one.</returns>
	public static string RetryAfterOf(AdmissionDecision decision)
	{
		var seconds = Math.Max(1, (int)Math.Ceiling((decision.RetryAfter ?? TimeSpan.Zero).TotalSeconds));
		return seconds.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>Gets the kind of work an endpoint declared, or the ordinary kind when it declared none.</summary>
	/// <param name="metadata">The metadata of the endpoint, the most specific declaration last.</param>
	/// <returns>The work class.</returns>
	public static WorkClass ClassOf(IEnumerable<object> metadata)
	{
		return metadata.OfType<AdmissionPolicyAttribute>().LastOrDefault()?.Class ?? WorkClass.Interactive;
	}
}