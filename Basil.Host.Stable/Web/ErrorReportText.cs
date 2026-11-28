namespace Basil.Host.Stable.Web;

internal static class ErrorReportText
{
	/// <summary>Cuts a client-written text to the length a report may log and replaces its control characters with spaces.</summary>
	public static string Clean(string text)
	{
		if (text.Length > OsuWebReplies.MaxErrorFieldLength) text = text[..OsuWebReplies.MaxErrorFieldLength];
		return string.Create(text.Length, text, static (span, source) =>
		{
			for (var i = 0; i < span.Length; i++) span[i] = char.IsControl(source[i]) ? ' ' : source[i];
		});
	}
}
