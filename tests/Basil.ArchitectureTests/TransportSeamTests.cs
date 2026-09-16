using Basil.Infrastructure.Shared.Persistence;
using Basil.Application.Sessions;
using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     Pins the seam between business code and the bancho and IRC wire formats. A type that
///     decides <em>what</em> happens must not also encode <em>how the client hears about it</em>;
///     today a fixed set of services does both, and this test makes that set visible so it can
///     only shrink.
/// </summary>
public class TransportSeamTests
{
	/// <summary>Every slice except <c>Irc</c>, which is a transport and not a feature.</summary>
	private static readonly string[] BusinessSlices =
	[
		"Auth", "Beatmaps", "Bot", "Chat", "Content", "Diagnostics",
		"Multiplayer", "Scores", "Spectating", "Users"
	];

	/// <summary>
	///     Everything under the business slices except the packet handlers (a <c>.Packets</c>
	///     namespace in every slice). Those are the adapters the protocol exists for; everything
	///     else is business code or an HTTP surface, and neither has a reason to know a wire format.
	/// </summary>
	private static Conditions BusinessAndApiTypes()
	{
		var businessSlicePattern = $@"^Basil\.Infrastructure\.({string.Join('|', BusinessSlices)})(\.|$)";

		return Types.InAssembly(typeof(SqlMigrationRunner).Assembly)
			.That().ResideInNamespaceMatching(businessSlicePattern)
			.And().DoNotResideInNamespaceContaining(".Packets")
			.Should();
	}

	[Fact]
	public void Business_And_Api_Types_Should_Not_Reference_Protocol()
	{
		// Every type below encodes bancho packets (ServerPacketWriter) or IRC lines
		// (IrcMessageWriter), or carries a wire record (MatchPacket, MatchState) through business
		// code. Each is a real coupling that predates the migration: the services decide an outcome
		// and, in the same method, choose the packet that announces it. The two route types are the
		// API host describing bancho wire structures. This list pins the set so it can only shrink:
		// a new business type reaching for the protocol fails the build, and removing an entry
		// (the seam work that replaces an encoder call with a notification contract) fails too,
		// as a reminder to delete its row here.
		string[] knownOffenders =
		[
			"Basil.Infrastructure.Content.AnnounceRoutes",
			"Basil.Infrastructure.Multiplayer.MatchPacketDataMapper",
			"Basil.Infrastructure.Spectating.SpectateFramesEvent"
		];

		var result = BusinessAndApiTypes()
			.NotHaveDependencyOn("Basil.Protocol")
			.GetResult();

		var actualOffenders = (result.FailingTypes ?? [])
			.Select(t => t.FullName)
			.OfType<string>()
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();

		Assert.Equal(knownOffenders.OrderBy(name => name, StringComparer.Ordinal), actualOffenders);
	}

	[Fact]
	public void Application_Types_Should_Not_Reference_Protocol()
	{
		// LoginResponseEncoder/PacketBuilders build bancho packets and are the Application-side
		// equivalent of Business_And_Api_Types_Should_Not_Reference_Protocol's pinned Infrastructure
		// list above — deliberately allowed here, pinned so the set can only shrink.
		// BanchoIrcBridgeConnection, IIrcConnection, IrcAuthenticationService, IrcLoginOutcome,
		// IrcNamesReply, and IrcQueryService are IRC-side seam types that build or carry IrcMessage,
		// pinned (U3).
		string[] knownOffenders =
		[
			"Basil.Application.Auth.LoginResponseEncoder",
			"Basil.Application.Auth.PacketBuilders",
			"Basil.Application.Irc.BanchoIrcBridgeConnection",
			"Basil.Application.Irc.IIrcConnection",
			"Basil.Application.Irc.IrcAuthenticationService",
			"Basil.Application.Irc.IrcLoginOutcome",
			"Basil.Application.Irc.IrcNamesReply",
			"Basil.Application.Irc.IrcQueryService"
		];

		var result = Types.InAssembly(typeof(GameSession).Assembly)
			.That().DoNotResideInNamespaceContaining(".Packets")
			.ShouldNot().HaveDependencyOn("Basil.Protocol")
			.GetResult();

		var actualOffenders = (result.FailingTypes ?? [])
			.Select(t => t.FullName)
			.OfType<string>()
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();

		Assert.Equal(knownOffenders.OrderBy(name => name, StringComparer.Ordinal), actualOffenders);
	}
}