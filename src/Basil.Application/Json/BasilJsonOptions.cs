using System.Text.Json;
using Basil.Application.Services.Multiplayer;

namespace Basil.Application.Json;

/// <summary>
///     The shared <see cref="JsonSerializerOptions" /> instance that every live-payload
///     serialization should use.
/// </summary>
/// <remarks>
///     Consumed by <see cref="SnapshotChannel{T}" /> and the RFC 7396 merge-patch serialization
///     (full snapshots and deltas), and by the packet handlers that publish onto those channels
///     (<c>MatchScoreUpdateHandler</c> and <c>SpectateFramesHandler</c>). It uses the web defaults
///     (camelCase naming) plus the <see cref="CountryJsonConverter" /> and
///     <see cref="TimeSpanSecondsJsonConverter" /> converters, so a Country and a TimeSpan
///     serialize the same way in every payload.
/// </remarks>
public static class BasilJsonOptions
{
	/// <summary>Gets the shared <see cref="JsonSerializerOptions" /> instance for live-payload serialization.</summary>
	public static readonly JsonSerializerOptions Instance = CreateOptions();

	private static JsonSerializerOptions CreateOptions()
	{
		var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		options.Converters.Add(new CountryJsonConverter());
		options.Converters.Add(new TimeSpanSecondsJsonConverter());
		return options;
	}
}