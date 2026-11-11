using Basil.Gateway;
using Basil.Host.Irc;
using Basil.Host.Rest;
using Basil.Host.Stable;
using Basil.Host.Stable.Packets;
using Basil.Host.Stable.Web;
using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Common.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Basil.ArchitectureTests;

/// <summary>
///     Every entry point declares an admission policy, by carrying <see cref="AdmissionPolicyAttribute" /> in its
///     metadata: each route of the HTTP hosts, each packet type of the osu! client and each IRC command.
/// </summary>
public sealed class AdmissionPolicyTests
{
	private static IEnumerable<EntryPoint> Undeclared(IEnumerable<EntryPoint> entryPoints)
	{
		return entryPoints.Where(entry => !entry.Metadata.OfType<AdmissionPolicyAttribute>().Any());
	}

	// The services are the composition's: the route handlers' parameters are inferred from what is registered.
	private static List<EntryPoint> Routes(string host, Action<IEndpointRouteBuilder> map)
	{
		var builder = WebApplication.CreateBuilder();
		builder.Services.AddSingleton(Options.Create(new StorageOptions { Location = Path.GetTempPath() }));
		builder.Services.AddSingleton(
			Options.Create(new DatabaseOptions { WriteConnectionString = "Host=localhost;Database=basil" }));
		builder.Services.AddBasil();
		builder.Services.AddRestApi(builder.Configuration);
		using var app = builder.Build();
		map(app);
		return ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
			.Select(endpoint => new EntryPoint(host, endpoint.DisplayName ?? "?", endpoint.Metadata.ToList()))
			.ToList();
	}

	private static List<EntryPoint> Rest()
	{
		return Routes("Rest", RestHost.MapRest);
	}

	private static List<EntryPoint> Stable()
	{
		var routes = Routes("Stable", StableRoutes.MapStable);
		routes.AddRange(Routes("Stable", OsuWebRoutes.MapOsuWeb));
		routes.AddRange(
			PacketHandlers.EntryPoints.Select(entry => new EntryPoint("Stable", entry.Name, entry.Metadata)));
		return routes;
	}

	private static List<EntryPoint> Irc()
	{
		return [.. IrcCommands.EntryPoints.Select(entry => new EntryPoint("Irc", entry.Name, entry.Metadata))];
	}

	[Fact]
	public void EntryNoPolicy_Detected()
	{
		var entryPoints = new[]
		{
			new EntryPoint("Rest", "GET /fake", []),
			new EntryPoint("Rest", "GET /declared", [new AdmissionPolicyAttribute()])
		};

		Assert.Equal(["GET /fake"], Undeclared(entryPoints).Select(entry => entry.Name));
	}

	[Fact]
	public void RouteNoPolicy_Detected()
	{
		var routes = Routes("Rest", app => app.MapGet("/fake", () => "x"));

		Assert.Single(Undeclared(routes));
	}

	[Fact]
	public void EveryRestRoute_DeclaresAPolicy()
	{
		var routes = Rest();

		Assert.True(routes.Count > 50, $"only {routes.Count} Rest routes were enumerated");
		Assert.Empty(Undeclared(routes));
	}

	[Fact]
	public void AndPacketType_DeclaresAPolicy()
	{
		var entryPoints = Stable();

		Assert.True(entryPoints.Count > 50, $"only {entryPoints.Count} Stable entry points were enumerated");
		Assert.Empty(Undeclared(entryPoints));
	}

	[Fact]
	public void EveryIrcCommand_DeclaresAPolicy()
	{
		var commands = Irc();

		Assert.True(commands.Count > 20, $"only {commands.Count} IRC commands were enumerated");
		Assert.Empty(Undeclared(commands));
	}

	private sealed record EntryPoint(string Host, string Name, IReadOnlyList<object> Metadata);
}