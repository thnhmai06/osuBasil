using System.Text.Json;
using Basil.Application.Formats;

namespace Basil.Application.Tests.Formats;

/// <summary>Verifies the shared serializer options used by every live-payload and API response body.</summary>
public class BasilJsonOptionsTests
{
	/// <summary>
	///     Regression test: the default <see cref="JsonSerializerOptions" /> web encoder escapes a
	///     literal <c>+</c> as a 6-character Unicode escape sequence, needlessly bloating and
	///     obscuring any string field (chat text, beatmap titles, usernames) that contains one. The
	///     relaxed encoder is safe here since every consumer parses this as JSON, never embeds it
	///     into an HTML document.
	/// </summary>
	[Fact]
	public void Instance_DoesNotEscapePlusSign()
	{
		var json = JsonSerializer.Serialize("a+b", BasilJsonOptions.Instance);

		Assert.Equal("\"a+b\"", json);
	}
}
