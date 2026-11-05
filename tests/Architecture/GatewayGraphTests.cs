using System.Xml.Linq;

namespace Basil.ArchitectureTests;

/// <summary>
///     <c>Basil.Gateway</c> is a leaf: it references no other Basil project and no ASP.NET Core, so the hosts and
///     <c>Basil.Host.Shared</c> may depend on it and it can never depend back on them.
/// </summary>
public class GatewayGraphTests
{
	[Fact]
	public void GatewayReferencesNoOtherProjectAndNoAspNetCore()
	{
		Assert.Empty(Violations(GatewayProject()));
	}

	[Fact]
	public void TheRuleRefusesAProjectThatBreaksIt()
	{
		var project = XDocument.Parse("""
		                              <Project>
		                                <ItemGroup>
		                                  <ProjectReference Include="..\Domain\Basil.Domain.csproj"/>
		                                  <FrameworkReference Include="Microsoft.AspNetCore.App"/>
		                                  <PackageReference Include="Microsoft.AspNetCore.Http.Abstractions"/>
		                                </ItemGroup>
		                              </Project>
		                              """);

		Assert.Equal(3, Violations(project).Count());
	}

	[Fact]
	public void TheMeasureSeesTheGatewayProject()
	{
		Assert.Contains(GatewayProject().Descendants(), element => element.Name.LocalName == "RootNamespace" &&
		                                                           element.Value == "Basil.Gateway");
	}

	private static IEnumerable<string> Violations(XDocument project)
	{
		return project.Descendants()
			.Where(element =>
				element.Name.LocalName is "ProjectReference" or "FrameworkReference" or "PackageReference")
			.Where(element => element.Name.LocalName == "ProjectReference" ||
			                  element.Attribute("Include")!.Value.StartsWith("Microsoft.AspNetCore",
				                  StringComparison.Ordinal))
			.Select(element => element.Attribute("Include")!.Value);
	}

	private static XDocument GatewayProject()
	{
		return XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Gateway", "Basil.Gateway.csproj"));
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Basil.slnx")))
			directory = directory.Parent;
		return directory?.FullName ?? throw new DirectoryNotFoundException("Basil.slnx was not found.");
	}
}