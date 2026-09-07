using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces the slice-boundary rules the vertical-slice architecture depends on: a slice may
///     only reference the slices declared in <see cref="SliceAdjacency" />, and <c>Shared</c> may
///     not become a second, undeclared way to couple slices together.
/// </summary>
public class SliceBoundaryTests
{
	private const string FeaturePrefix = "Basil.Server.Features.";

	[Fact]
	public void Slices_Should_Only_Reference_Declared_Slices()
	{
		var assembly = typeof(Basil.Server.Program).Assembly;
		var allSlices = Types.InAssembly(assembly).GetTypes()
			.Where(t => t.Namespace?.StartsWith(FeaturePrefix, StringComparison.Ordinal) == true)
			.Select(t => t.Namespace![FeaturePrefix.Length..].Split('.')[0])
			.Distinct()
			.ToArray();

		var violations = new List<string>();

		foreach (var type in Types.InAssembly(assembly).GetTypes()
			         .Where(t => t.Namespace?.StartsWith(FeaturePrefix, StringComparison.Ordinal) == true))
		{
			var from = type.Namespace![FeaturePrefix.Length..].Split('.')[0];
			var forbidden = OtherSlices(allSlices, from, SliceAdjacency.Allowed);
			if (forbidden.Length == 0) continue;

			var result = Types.InAssembly(assembly)
				.That().HaveName(type.Name).And().ResideInNamespace(type.Namespace)
				.Should().NotHaveDependencyOnAny(forbidden)
				.GetResult();

			if (!result.IsSuccessful)
				violations.Add(
					$"{type.FullName} -> {string.Join(", ", result.FailingTypeNames ?? [])}");
		}

		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	/// <summary>Returns <c>Basil.Server.Features.&lt;X&gt;</c> for every slice that is neither <paramref name="from" /> nor an allowed target of <paramref name="from" />.</summary>
	private static string[] OtherSlices(string[] allSlices, string from, (string From, string To)[] allowed)
	{
		var allowedTargets = allowed.Where(edge => edge.From == from).Select(edge => edge.To).ToHashSet();
		return allSlices
			.Where(slice => slice != from && !allowedTargets.Contains(slice))
			.Select(slice => $"{FeaturePrefix}{slice}")
			.ToArray();
	}

	[Fact]
	public void Shared_Should_Not_Reference_Features()
	{
		// Shared/ still holds 14 types that reach into Features/. Each is a real structural
		// coupling that predates this migration and is out of Phase 0's scope to fix:
		// GameSession/UserSession/PlayerLogoutService/GhostDisconnectService hold a live
		// MatchSession and the IRC bridge connection (Task 1.4 -- MatchSession model
		// encapsulation -- is the task that owns unwinding this); the Http host-route files
		// (ApiHostRoutes, AssetsHostRoutes, BanchoHostGroups, OsuWebRoutes,
		// Bancho.PacketDispatcher, OpenApi.OpenApiExampleExtensions) inline slice logic directly
		// instead of only delegating to it; the Media asset providers and
		// FileSystemReplayStorage call slice services directly. This test pins the list so it can
		// only shrink -- a new Shared -> Features edge fails the build, and removing an offender
		// (a later phase's real fix) fails too, as a reminder to delete its entry here.
		string[] knownOffenders =
		[
			"Basil.Server.Shared.Http.ApiHostRoutes",
			"Basil.Server.Shared.Http.AssetsHostRoutes",
			"Basil.Server.Shared.Http.Bancho.PacketDispatcher",
			"Basil.Server.Shared.Http.BanchoHostGroups",
			"Basil.Server.Shared.Http.OpenApi.OpenApiExampleExtensions",
			"Basil.Server.Shared.Http.OsuWebRoutes",
			"Basil.Server.Shared.Media.Assets.BeatmapsetBackgroundProvider",
			"Basil.Server.Shared.Media.Assets.BeatmapThumbnailProvider",
			"Basil.Server.Shared.Media.Assets.MenuIconProvider",
			"Basil.Server.Shared.Sessions.GameSession",
			"Basil.Server.Shared.Sessions.GhostDisconnectService",
			"Basil.Server.Shared.Sessions.PlayerLogoutService",
			"Basil.Server.Shared.Sessions.UserSession",
			"Basil.Server.Shared.Storage.FileSystemReplayStorage"
		];

		var result = Types.InAssembly(typeof(Basil.Server.Program).Assembly)
			.That().ResideInNamespaceStartingWith("Basil.Server.Shared")
			.Should().NotHaveDependencyOn("Basil.Server.Features")
			.GetResult();

		var actualOffenders = (result.FailingTypes ?? [])
			.Select(t => t.FullName)
			.OfType<string>()
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();

		Assert.Equal(knownOffenders.OrderBy(name => name, StringComparer.Ordinal), actualOffenders);
	}

	/// <summary>
	///     Shared may only contain these concerns. The cross-slice allowlist creates an obvious escape
	///     hatch: when an edge is inconvenient to declare, the temptation is to move the code into
	///     Shared instead, which trades dependency spaghetti for a shared god layer. A fixed segment
	///     list makes Shared/Users/ or Shared/Matches/ a build failure, so adding a genuinely new
	///     cross-cutting concern is a visible decision rather than a drift.
	/// </summary>
	[Fact]
	public void Shared_Segments_Should_Come_From_The_Allowlist()
	{
		string[] allowed =
		[
			"Eventing", "Sessions", "Persistence", "Localization",
			"Logging", "Http", "Configuration", "Media", "Storage"
		];

		const string prefix = "Basil.Server.Shared.";
		var offenders = Types.InAssembly(typeof(Basil.Server.Program).Assembly).GetTypes()
			.Select(t => t.Namespace)
			// A type declared directly under the bare "Basil.Server.Shared" namespace (no
			// segment at all, e.g. BasilMetrics.cs) is not a segment and needs no allowlist entry.
			.Where(n => n is not null && n != "Basil.Server.Shared" &&
			            n.StartsWith(prefix, StringComparison.Ordinal))
			.Select(n => n![prefix.Length..].Split('.')[0])
			.Distinct()
			.Where(segment => !allowed.Contains(segment))
			.ToArray();

		Assert.True(offenders.Length == 0,
			$"Shared gained an unapproved concern: {string.Join(", ", offenders)}");
	}
}
