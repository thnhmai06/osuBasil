namespace Basil.ArchitectureTests;

/// <summary>
///     The cross-namespace references <c>Basil.Domain</c> permits, each one a deliberate decision.
/// </summary>
/// <remarks>
///     Mirrors <see cref="SliceAdjacency" />'s shape one level down: a namespace under
///     <c>Basil.Domain</c> is not forbidden from referencing another one -- a round genuinely has a
///     beatmap, a submission genuinely needs the client details captured at login -- because a ban
///     would be unenforceable and blanket permission would make the rule meaningless. Every edge is
///     named here instead, so an undeclared one fails the build.
/// </remarks>
/// <remarks>
///     Measured 2026-09-10, before Task C1 moves any file into <c>Basil.Domain</c>. This list is
///     Task C6's pinned starting point, not an empty allowlist grown from scratch: every namespace
///     under <c>Basil.Domain</c> today, including ones with no <c>Features/</c> counterpart
///     (<c>Channels</c>, <c>Login</c>, <c>Social</c>), is in the population
///     <see cref="DomainBoundaryTests.Namespaces_Should_Only_Reference_Declared_Namespaces" />
///     checks, and two of the six rows below (<c>Channels -> Users</c>, <c>Users -> Login</c>) exist
///     precisely because a namespace without a slice counterpart is not exempt.
/// </remarks>
internal static class DomainAdjacency
{
	public static readonly (string From, string To)[] Allowed =
	[
		// Round.cs describes the beatmap a round is played on.
		("Multiplayer", "Beatmaps"),

		// Round.cs carries the Submission each player produced for the round.
		("Multiplayer", "Scores"),

		// Submission.cs, HitCounts.cs and Mods.cs describe the beatmap a score was set on.
		("Scores", "Beatmaps"),

		// Submission.ValidateClientDetails checks the submission's ClientDetails against the
		// osu! version captured at login.
		("Scores", "Login"),

		// Channel.cs gates read/write access on the UserPrivileges level required to use it.
		("Channels", "Users"),

		// User.cs carries the Country resolved at login.
		("Users", "Login")
	];
}
