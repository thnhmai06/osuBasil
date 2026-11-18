using System.Reflection;

namespace Basil.ArchitectureTests;

public class ImplementationPlacementTests
{
	private static readonly string[] ImplementationFolders = ["Handlers", "Logic", "Workers"];

	private static readonly string[] ImplementationInterfaces =
	[
		"Mediator.INotificationHandler`1", "Mediator.IRequestHandler`2", "Microsoft.Extensions.Hosting.IHostedService"
	];

	private static readonly IReadOnlyList<Assembly> ModuleInfrastructures =
		ModuleAssemblies.Infrastructures.Where(assembly => assembly.GetName().Name != "Basil.Infrastructure.Basil.Shared")
			.ToArray();

	[Fact]
	public void ApplicationHandlerFolder_Absent()
	{
		var misplaced = ModuleAssemblies.Applications.SelectMany(assembly => assembly.GetTypes())
			.Where(InImplementationFolder).Select(type => type.FullName);

		Assert.Empty(misplaced);
	}

	[Fact]
	public void ApplicationHandlerImplementation_Absent()
	{
		var misplaced = ModuleAssemblies.Applications.SelectMany(assembly => assembly.GetTypes())
			.Where(IsImplementation).Select(type => type.FullName);

		Assert.Empty(misplaced);
	}

	[Fact]
	public void DetectorSeesHandlers_PositiveControl()
	{
		var types = ModuleInfrastructures.SelectMany(assembly => assembly.GetTypes()).ToArray();

		Assert.Contains(types, IsImplementation);
		Assert.Contains(types, InImplementationFolder);
	}

	[Fact]
	public void InfrastructureExposure_RegistrationOnly()
	{
		var exposed = ModuleInfrastructures.SelectMany(assembly => assembly.GetExportedTypes())
			.Where(type => !IsRegistration(type) && !IsMigration(type) &&
			               !type.Name.EndsWith("Options", StringComparison.Ordinal))
			.Select(type => type.FullName);

		Assert.Empty(exposed);
	}

	[Fact]
	public void ExposureCheck_PositiveControl()
	{
		var exposed = ModuleAssemblies.Applications.SelectMany(assembly => assembly.GetExportedTypes())
			.Where(type => !IsRegistration(type));

		Assert.NotEmpty(exposed);
	}

	[Fact]
	public void ModuleInfrastructure_SelfRegisters()
	{
		foreach (var assembly in ModuleInfrastructures)
		{
			var module = assembly.GetName().Name!.Split('.')[2];
			var registration = assembly.GetType($"Basil.Infrastructure.{module}.DependencyInjection");

			Assert.NotNull(registration);
			Assert.Contains(registration.GetMethods(BindingFlags.Public | BindingFlags.Static),
				method => method.Name.StartsWith("Add", StringComparison.Ordinal) &&
				          method.Name.EndsWith("Infrastructure", StringComparison.Ordinal));
		}
	}

	private static bool InImplementationFolder(Type type)
	{
		var parts = (type.Namespace ?? string.Empty).Split('.');
		return parts.Length > 3 && ImplementationFolders.Contains(parts[3]);
	}

	private static bool IsImplementation(Type type)
	{
		return type is { IsAbstract: false, IsInterface: false } && type.GetInterfaces().Any(contract =>
			ImplementationInterfaces.Contains((contract.IsGenericType ? contract.GetGenericTypeDefinition() : contract)
				.FullName));
	}

	private static bool IsRegistration(Type type)
	{
		return type is { IsAbstract: true, IsSealed: true, Name: "DependencyInjection" };
	}

	private static bool IsMigration(Type type)
	{
		return type.BaseType?.FullName == "Microsoft.EntityFrameworkCore.Migrations.Migration";
	}
}