using System.Text.Encodings.Web;
using System.Text.Json;

namespace Basil.Application.Unresolved.Shared.Json;

public static class BasilJsonOptions
{
	/// <summary>Gets the shared <see cref="JsonSerializerOptions" /> instance for live-payload serialization.</summary>
	public static readonly JsonSerializerOptions Instance = CreateOptions();

	private static JsonSerializerOptions CreateOptions()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
		};
		options.Converters.Add(new CountryJsonConverter());
		options.Converters.Add(new TimeSpanSecondsJsonConverter());
		return options;
	}
}