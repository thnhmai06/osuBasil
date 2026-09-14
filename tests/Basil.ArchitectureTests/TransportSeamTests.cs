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
	/// <summary>
	///     Everything under <c>Features/</c> except the packet handlers (a <c>.Packets</c> namespace
	///     in every slice) and the whole <c>Irc</c> slice, which is a transport and not a feature.
	///     Those are the adapters the protocol exists for; everything else is business code or an
	///     HTTP surface, and neither has a reason to know a wire format.
	/// </summary>
	private static Conditions BusinessAndApiTypes() =>
		Types.InAssembly(typeof(Basil.Server.Host.Bootstrap).Assembly)
			.That().ResideInNamespaceStartingWith("Basil.Server.Features")
			.And().DoNotResideInNamespaceContaining(".Packets")
			.And().DoNotResideInNamespaceStartingWith("Basil.Server.Features.Irc")
			.Should();

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
			"Basil.Server.Features.Auth.LoginService",
			"Basil.Server.Features.Chat.ChannelMembershipService",
			"Basil.Server.Features.Chat.ChatDispatchService",
			"Basil.Server.Features.Chat.ChatDispatchService+ChannelReplySink",
			"Basil.Server.Features.Chat.ChatDispatchService+DmReplySink",
			"Basil.Server.Features.Content.AnnounceRoutes",
			"Basil.Server.Features.Multiplayer.Endpoints.MatchListEndpoints",
			"Basil.Server.Features.Multiplayer.IMatchRegistry",
			"Basil.Server.Features.Multiplayer.InMemoryMatchRegistry",
			"Basil.Server.Features.Multiplayer.MatchLifecycle",
			"Basil.Server.Features.Multiplayer.MatchLiveSnapshotBuilder",
			"Basil.Server.Features.Multiplayer.MatchPacketDataMapper",
			"Basil.Server.Features.Multiplayer.MpCommandService",
			"Basil.Server.Features.Multiplayer.MpCommandService+ScopedDmReplySink",
			"Basil.Server.Features.Spectating.SpectateFramesEvent"
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
}