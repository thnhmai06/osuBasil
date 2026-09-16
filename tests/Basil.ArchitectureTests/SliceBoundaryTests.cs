using Basil.Infrastructure.Shared.Persistence;
using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces the slice-boundary rules the vertical-slice architecture depends on: a slice may
///     only reference the slices declared in <see cref="SliceAdjacency" />, and <c>Shared</c> may
///     not become a second, undeclared way to couple slices together.
/// </summary>
public class SliceBoundaryTests
{
	private const string RootPrefix = "Basil.Infrastructure.";

	/// <summary>Top-level segments under <see cref="RootPrefix" /> that are not a slice.</summary>
	private static readonly string[] NonSliceSegments = ["Shared"];

	[Fact]
	public void Slices_Should_Only_Reference_Declared_Slices()
	{
		var assembly = typeof(SqlMigrationRunner).Assembly;
		var allSlices = Types.InAssembly(assembly).GetTypes()
			.Where(t => t.Namespace?.StartsWith(RootPrefix, StringComparison.Ordinal) == true)
			.Select(t => t.Namespace![RootPrefix.Length..].Split('.')[0])
			.Where(segment => !NonSliceSegments.Contains(segment))
			.Distinct()
			.ToArray();

		var violations = new List<string>();

		foreach (var type in Types.InAssembly(assembly).GetTypes()
			         .Where(t => t.Namespace?.StartsWith(RootPrefix, StringComparison.Ordinal) == true))
		{
			var from = type.Namespace![RootPrefix.Length..].Split('.')[0];
			if (NonSliceSegments.Contains(from)) continue;

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

	/// <summary>Returns <c>Basil.Infrastructure.&lt;X&gt;</c> for every slice that is neither <paramref name="from" /> nor an allowed target of <paramref name="from" />.</summary>
	private static string[] OtherSlices(string[] allSlices, string from, (string From, string To)[] allowed)
	{
		var allowedTargets = allowed.Where(edge => edge.From == from).Select(edge => edge.To).ToHashSet();
		return allSlices
			.Where(slice => slice != from && !allowedTargets.Contains(slice))
			.Select(slice => $"{RootPrefix}{slice}")
			.ToArray();
	}

	[Fact]
	public void Shared_Should_Not_Reference_Features()
	{
		// The types below still reach from Shared/ into Features/. Each is a real structural
		// coupling that predates this migration and is out of Phase 0's scope to fix:
		// GameSession/UserSession/GhostDisconnectService hold a live MatchSession and the IRC
		// bridge connection (Task 1.4 -- MatchSession model encapsulation -- is the task that
		// owns unwinding this; GhostDisconnectService only needs Irc.IrcSession as
		// ISessionRegistry<IrcSession>'s type argument, the same shape); BanchoHostGroups,
		// Bancho.PacketDispatcher, OsuWebRoutes and OpenApi.OpenApiExampleExtensions inline slice
		// logic directly instead of only delegating to it; the Media asset providers call slice
		// services directly. This test pins the list so it can only shrink -- a new Shared ->
		// Features edge fails the build, and removing an offender (a later phase's real fix) fails
		// too, as a reminder to delete its entry here.
		//
		// ApiHostRoutes and AssetsHostRoutes dropped out of this list in Task 0.6: they used to
		// both hold shared endpoints (health checks, docs, redirects) *and* delegate to slice-owned
		// route mappers (`group.MapMatchRoutes()` and peers). Task 0.6 moved every delegating call
		// out to Host/SliceRegistration.MapAll, which is Host composition code, not a Shared type,
		// so it isn't subject to this rule at all. What's left in both files is genuinely
		// shared -- no Features reference remains.
		//
		// FileSystemReplayStorage dropped out during C1b (2026-09-15): it referenced
		// Features.Scores.IReplayStorage, and that interface moved into Basil.Domain.Scores, so the
		// reference now points at Domain instead of a slice. OsuWebRoutes dropped out the same way:
		// its Features.Auth references (AdminKeyService, IPasswordHasher) all moved.
		string[] knownOffenders =
		[
			"Basil.Infrastructure.Shared.Http.Bancho.PacketDispatcher",
			"Basil.Infrastructure.Shared.Http.BanchoHostGroups",
			"Basil.Infrastructure.Shared.Http.OpenApi.OpenApiExampleExtensions",
			"Basil.Infrastructure.Shared.Http.OpenApi.SecuritySchemeTransformers",
			"Basil.Infrastructure.Shared.Media.Assets.BeatmapsetBackgroundProvider",
			"Basil.Infrastructure.Shared.Media.Assets.BeatmapThumbnailProvider",
			"Basil.Infrastructure.Shared.Media.Assets.MenuIconProvider",
			"Basil.Infrastructure.Shared.Sessions.GameSession",
			"Basil.Infrastructure.Shared.Sessions.GhostDisconnectService",
			"Basil.Infrastructure.Shared.Sessions.UserSession"
		];

		var featureNamespaces = SliceAdjacency.Allowed
			.Select(edge => edge.From)
			.Concat(SliceAdjacency.Allowed.Select(edge => edge.To))
			.Distinct()
			.Select(slice => $"{RootPrefix}{slice}")
			.ToArray();

		var result = Types.InAssembly(typeof(SqlMigrationRunner).Assembly)
			.That().ResideInNamespaceStartingWith($"{RootPrefix}Shared")
			.Should().NotHaveDependencyOnAny(featureNamespaces)
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

		var prefix = $"{RootPrefix}Shared.";
		var offenders = Types.InAssembly(typeof(SqlMigrationRunner).Assembly).GetTypes()
			.Select(t => t.Namespace)
			// A type declared directly under the bare "Basil.Infrastructure.Shared" namespace (no
			// segment at all, e.g. BasilMeter.cs) is not a segment and needs no allowlist entry.
			.Where(n => n is not null && n != $"{RootPrefix}Shared" &&
			            n.StartsWith(prefix, StringComparison.Ordinal))
			.Select(n => n![prefix.Length..].Split('.')[0])
			.Distinct()
			.Where(segment => !allowed.Contains(segment))
			.ToArray();

		Assert.True(offenders.Length == 0,
			$"Shared gained an unapproved concern: {string.Join(", ", offenders)}");
	}
}