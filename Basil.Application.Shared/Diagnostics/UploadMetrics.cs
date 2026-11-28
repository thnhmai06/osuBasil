using System.Diagnostics.Metrics;

namespace Basil.Application.Shared.Diagnostics;

/// <summary>Counts the uploads the server refused for their size or content, by the kind of refusal.</summary>
public static class UploadMetrics
{
	public const string RefusedName = "basil.uploads.refused";

	public const string ReasonTag = "reason";

	private static readonly Counter<long> Refused = new Meter("Basil.Uploads", "1.0")
		.CreateCounter<long>(RefusedName, description: "Uploads refused for their size or content.");

	/// <summary>Counts a refusal and hands the exception back to be thrown.</summary>
	public static TException Refuse<TException>(TException refusal) where TException : Exception
	{
		Refused.Add(1, new KeyValuePair<string, object?>(ReasonTag, typeof(TException).Name));
		return refusal;
	}
}
