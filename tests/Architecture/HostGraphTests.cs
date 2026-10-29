using System.Xml.Linq;

namespace Basil.ArchitectureTests;

/// <summary>
///     The project graph of the hosts: every transport shares <c>Basil.Host.Shared</c> and the transports stay
///     independent of each other; the API and the console also stay independent of the Bancho wire format. A transport
///     that breaks one of these edges is caught here, before the solution compiles against a wrong reference.
/// </summary>
public class HostGraphTests
{
	private const string Shared = "Basil.Host.Shared";
	private const string ProtocolBancho = "Basil.Protocol.Bancho";

	private static readonly string[] Transports =
		["Basil.Host.Bancho", "Basil.Host.Irc", "Basil.Host.Rest", "Basil.Host.Cli"];

	[Fact]
	public void EveryTransportReferencesTheSharedHostProject()
	{
		Assert.Empty(MissingShared(ProjectEdges()));
		// The control: a synthetic graph without the edge is reported.
		Assert.NotEmpty(MissingShared([("Basil.Host.Irc", "Basil.Host.Bancho")]));
	}

	[Fact]
	public void TransportsDoNotReferenceEachOther()
	{
		var forbidden = ProjectEdges().Where(edge => ReferEachOther(edge.From, edge.To))
			.Select(edge => $"{edge.From} -> {edge.To}");

		Assert.Empty(forbidden);
	}

	[Fact]
	public void RestAndCliDoNotReferenceTheBanchoProtocol()
	{
		var forbidden = ProjectEdges().Where(edge => NeedsBanchoProtocol(edge.From, edge.To))
			.Select(edge => $"{edge.From} -> {edge.To}");

		Assert.Empty(forbidden);
	}

	[Fact]
	public void TheMeasureSeesAnEdgeItShould()
	{
		Assert.Contains(("Basil.Host.Bancho", ProtocolBancho), ProjectEdges());
		Assert.Contains(("Basil.Host.Bancho", Shared), ProjectEdges());
	}

	[Fact]
	public void TheRulesRefuseAnEdgeTheyShould()
	{
		Assert.True(ReferEachOther("Basil.Host.Bancho", "Basil.Host.Irc"));
		Assert.True(ReferEachOther("Basil.Host.Irc", "Basil.Host.Rest"));
		Assert.True(NeedsBanchoProtocol("Basil.Host.Rest", ProtocolBancho));
		Assert.True(NeedsBanchoProtocol("Basil.Host.Cli", ProtocolBancho));
		Assert.False(NeedsBanchoProtocol("Basil.Host.Bancho", ProtocolBancho));
		Assert.False(NeedsBanchoProtocol("Basil.Host.Irc", ProtocolBancho));
		Assert.False(ReferEachOther("Basil.Host.Bancho", Shared));
	}

	private static IEnumerable<string> MissingShared(IEnumerable<(string From, string To)> edges)
	{
		var present = edges.ToHashSet();
		return Transports.Where(transport => !present.Contains((transport, Shared)));
	}

	private static bool ReferEachOther(string from, string to)
	{
		return Transports.Contains(from) && Transports.Contains(to);
	}

	private static bool NeedsBanchoProtocol(string from, string to)
	{
		return to == ProtocolBancho && from is "Basil.Host.Rest" or "Basil.Host.Cli";
	}

	private static (string From, string To)[] ProjectEdges()
	{
		return Transports.SelectMany(project =>
		{
			var file = Path.Combine(RepositoryRoot(), "src", "Hosts", project["Basil.Host.".Length..],
				project + ".csproj");
			return XDocument.Load(file).Descendants()
				.Where(element => element.Name.LocalName == "ProjectReference")
				.Select(element => (From: project,
					To: Path.GetFileNameWithoutExtension(
						element.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar))));
		}).ToArray();
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException("Basil.slnx was not found.");
	}
}
