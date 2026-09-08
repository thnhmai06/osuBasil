using Basil.Server.Shared.Http;

namespace Basil.Server.Host;

/// <summary>Registers HTTP JSON serialization options for the host.</summary>
internal static class JsonSetup
{
	/// <summary>
	///     Registers <see cref="CountryJsonConverter" /> globally for every JSON response using the host defaults.
	/// </summary>
	/// <remarks>
	///     <see cref="CountryJsonConverter" /> is the only enum with a non-default wire form; every
	///     other enum keeps System.Text.Json's default numeric serialization. Regular JSON responses
	///     and live SSE payloads always agree on this, since both serialize with the same converter set.
	/// </remarks>
	/// <param name="builder">The web application builder whose HTTP JSON options are configured.</param>
	public static void Configure(WebApplicationBuilder builder)
	{
		// The framework's JsonOptions.SerializerOptions has no public setter, so the instance itself
		// can't be swapped for BasilJsonOptions.Instance (the shared options every live SSE payload
		// serializes with); copying its converters is the closest equivalent.
		builder.Services.ConfigureHttpJsonOptions(options =>
		{
			foreach (var converter in BasilJsonOptions.Instance.Converters)
				options.SerializerOptions.Converters.Add(converter);
		});
	}
}