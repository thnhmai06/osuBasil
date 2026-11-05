using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basil.Host.Shared;

/// <summary>The serializer options of the API's records: camelCase names, enums as camelCase strings.</summary>
public static class ApiJson
{
	/// <summary>Gets the options.</summary>
	public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		// The API is JSON over HTTPS read by programs: "+" and non-ASCII letters are not escaped to \uXXXX.
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};
}