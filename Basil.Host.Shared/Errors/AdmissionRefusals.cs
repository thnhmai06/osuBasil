using System.Globalization;
using Basil.Gateway.Admission;
using Basil.Gateway.Limits;

namespace Basil.Host.Shared.Errors;

public static class AdmissionRefusals
{
	public const string RateLimitedCode = "rateLimited";

	public const string RateLimitedMessage = "Too many requests.";

	public const string BusyCode = "busy";

	public const string BusyMessage = "The server is busy. Try again later.";

	public static int StatusOf(AdmissionDecision decision)
	{
		return decision.Reason == AdmissionDenial.RateExceeded ? 429 : 503;
	}

	public static string CodeOf(AdmissionDecision decision)
	{
		return decision.Reason == AdmissionDenial.RateExceeded ? RateLimitedCode : BusyCode;
	}

	public static string MessageOf(AdmissionDecision decision)
	{
		return decision.Reason == AdmissionDenial.RateExceeded ? RateLimitedMessage : BusyMessage;
	}

	public static string RetryAfterOf(AdmissionDecision decision)
	{
		var seconds = Math.Max(1, (int)Math.Ceiling((decision.RetryAfter ?? TimeSpan.Zero).TotalSeconds));
		return seconds.ToString(CultureInfo.InvariantCulture);
	}

	public static WorkClass ClassOf(IEnumerable<object> metadata)
	{
		return metadata.OfType<AdmissionPolicyAttribute>().LastOrDefault()?.Class ?? WorkClass.Interactive;
	}
}