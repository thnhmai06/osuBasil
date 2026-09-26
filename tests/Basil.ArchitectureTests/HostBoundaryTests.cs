using System.Reflection;
using Basil.Host.Api.Shared.Http;
using Basil.Host.Bancho;
using Basil.Host.Irc;
using NetArchTest.Rules;

namespace Basil.ArchitectureTests;

/// <summary>
///     Enforces that the three host projects -- <c>Basil.Host.Bancho</c>, <c>Basil.Host.Irc</c>,
///     <c>Basil.Host.Api</c> -- stay siblings, not dependencies of one another. Every one of them
///     is wired into the composition root (<c>Basil.Host</c>) side by side; a reference from one
///     host into another would mean the two can no longer be composed independently, the exact
///     shape the split into three host projects exists to avoid.
/// </summary>
/// <remarks>
///     <c>Basil.Host.Api</c> additionally may not depend on either protocol project: unlike
///     <c>Basil.Host.Bancho</c>, none of its routes speak a wire protocol, so a reference here
///     would mean an HTTP endpoint reaching for packet types it has no business building. The
///     check is a plain <c>"Basil.Protocol"</c> namespace prefix -- <c>Basil.Protocol.Bancho</c>'s
///     own root namespace is bare <c>Basil.Protocol</c> (no <c>.Bancho</c> suffix), so this also
///     catches <c>Basil.Protocol.Irc</c>, which is fine: Host.Api has no business with either.
///     One offender is pinned rather than fixed, mirroring
///     <see cref="TransportSeamTests.Application_Types_Should_Not_Reference_Protocol" />'s own
///     <c>SpectateFramesEvent</c> exception for the same reason: <c>OpenApiExampleExtensions</c>
///     builds its `input`-event OpenAPI examples by serializing a real
///     <c>SpectateFramesEvent</c> -- itself already pinned there for carrying <c>ReplayFrame</c>/
///     <c>ScoreFrame</c> directly -- so the documented example always matches the endpoint's
///     actual wire shape instead of a handwritten literal that could silently drift from it.
/// </remarks>
public class HostBoundaryTests
{
	private static readonly Assembly BanchoAssembly = typeof(BanchoHostServiceCollectionExtensions).Assembly;
	private static readonly Assembly IrcAssembly = typeof(IrcHostServiceCollectionExtensions).Assembly;
	private static readonly Assembly ApiAssembly = typeof(ApiHostRoutes).Assembly;

	[Fact]
	public void Bancho_Should_Not_HaveDependencyOn_OtherHosts()
	{
		var result = Types.InAssembly(BanchoAssembly)
			.Should()
			.NotHaveDependencyOnAny("Basil.Host.Irc", "Basil.Host.Api")
			.GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void Irc_Should_Not_HaveDependencyOn_OtherHosts()
	{
		var result = Types.InAssembly(IrcAssembly)
			.Should()
			.NotHaveDependencyOnAny("Basil.Host.Bancho", "Basil.Host.Api")
			.GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void Api_Should_Not_HaveDependencyOn_OtherHosts()
	{
		var result = Types.InAssembly(ApiAssembly)
			.Should()
			.NotHaveDependencyOnAny("Basil.Host.Bancho", "Basil.Host.Irc")
			.GetResult();

		Assert.True(result.IsSuccessful, FailureMessage(result));
	}

	[Fact]
	public void Api_Should_Not_HaveDependencyOn_ProtocolBancho()
	{
		string[] knownOffenders = ["Basil.Host.Api.Shared.Http.OpenApi.OpenApiExampleExtensions"];

		var result = Types.InAssembly(ApiAssembly)
			.Should()
			.NotHaveDependencyOnAny("Basil.Protocol")
			.GetResult();

		var actualOffenders = (result.FailingTypes ?? [])
			.Select(t => t.FullName)
			.OfType<string>()
			.OrderBy(name => name, StringComparer.Ordinal)
			.ToArray();

		Assert.Equal(knownOffenders.OrderBy(name => name, StringComparer.Ordinal), actualOffenders);
	}

	private static string FailureMessage(NetArchTest.Rules.TestResult result)
	{
		if (result.IsSuccessful) return string.Empty;

		var offenders = result.FailingTypes is null
			? "unknown"
			: string.Join(", ", result.FailingTypes.Select(t => t.FullName));

		return $"Architecture rule violated by: {offenders}";
	}
}