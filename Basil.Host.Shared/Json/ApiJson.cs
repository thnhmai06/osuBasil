using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basil.Host.Shared.Json;

public static class ApiJson
{
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		// The API is JSON over HTTPS read by programs: "+" and non-ASCII letters are not escaped to \uXXXX.
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};
}