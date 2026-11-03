using Basil.Configuration;
using Microsoft.Extensions.Configuration;

namespace Basil.Tests.Configuration;

/// <summary>The database settings: two connection strings and their pools.</summary>
public sealed class DatabaseSetupTests
{
	private static IConfiguration Settings(params (string Key, string Value)[] values)
	{
		return new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(pair => pair.Key,
			pair => (string?)pair.Value)).Build();
	}

	[Fact]
	public void TheWriteAndReadConnectionStringsAreTakenFromConnectionStrings()
	{
		var options = DatabaseSetup.Read(Settings(("ConnectionStrings:Write", "Host=primary"),
			("ConnectionStrings:Read", "Host=replica")));

		Assert.Equal("Host=primary", options.WriteConnectionString);
		Assert.Equal("Host=replica", options.ReadConnectionString);
	}

	[Fact]
	public void WithoutAReadConnectionStringReadsUseTheWriteOne()
	{
		var options = DatabaseSetup.Read(Settings(("ConnectionStrings:Write", "Host=primary")));

		Assert.True(string.IsNullOrEmpty(options.ReadConnectionString));
	}

	[Fact]
	public void ThePoolSizesDefaultToTheDriverForReadsAndEightForWrites()
	{
		var options = DatabaseSetup.Read(Settings(("ConnectionStrings:Write", "Host=primary")));

		Assert.Equal(0, options.ReadPoolSize);
		Assert.Equal(8, options.WritePoolSize);
	}

	[Fact]
	public void ThePoolSizesAreTakenFromTheDatabaseSection()
	{
		var options = DatabaseSetup.Read(Settings(("ConnectionStrings:Write", "Host=primary"),
			("Basil:Database:Read:PoolSize", "20"), ("Basil:Database:Write:PoolSize", "3")));

		Assert.Equal(20, options.ReadPoolSize);
		Assert.Equal(3, options.WritePoolSize);
	}

	[Fact]
	public void AMissingWriteConnectionStringStopsTheServerWithAClearError()
	{
		var exception = Assert.Throws<InvalidOperationException>(() => DatabaseSetup.Read(Settings(
			("ConnectionStrings:Read", "Host=replica"))));

		Assert.Contains("ConnectionStrings:Write", exception.Message);
	}
}