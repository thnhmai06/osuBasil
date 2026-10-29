using Microsoft.AspNetCore.Builder;

namespace Basil.Host.Tests.Configuration;

/// <summary>The order in which the server's configuration sources win: the environment and the command line over the files.</summary>
public sealed class ConfigurationSetupTests
{
	private const string Key = "Basil__Database__ConnectionString";
	private const string FileValue = "Host=file;Username=file";
	private const string EnvironmentValue = "Host=environment;Username=environment";

	[Fact]
	public void AnEnvironmentVariableWinsOverTheConfigurationFile()
	{
		var file = Path.Combine(ConfigurationSetup.Directory, "appsettings.json");
		var previous = File.Exists(file) ? File.ReadAllText(file) : null;
		var previousEnvironment = Environment.GetEnvironmentVariable(Key);
		try
		{
			Directory.CreateDirectory(ConfigurationSetup.Directory);
			File.WriteAllText(file, $$"""{ "Basil": { "Database": { "ConnectionString": "{{FileValue}}" } } }""");
			Environment.SetEnvironmentVariable(Key, null);

			var withoutEnvironment = WebApplication.CreateBuilder([]);
			ConfigurationSetup.Add(withoutEnvironment, []);
			// The control: without the variable the file's value is the one in effect.
			Assert.Equal(FileValue, withoutEnvironment.Configuration["Basil:Database:ConnectionString"]);

			Environment.SetEnvironmentVariable(Key, EnvironmentValue);
			var withEnvironment = WebApplication.CreateBuilder([]);
			ConfigurationSetup.Add(withEnvironment, []);
			Assert.Equal(EnvironmentValue, withEnvironment.Configuration["Basil:Database:ConnectionString"]);
		}
		finally
		{
			Environment.SetEnvironmentVariable(Key, previousEnvironment);
			if (previous is null) File.Delete(file);
			else File.WriteAllText(file, previous);
		}
	}
}