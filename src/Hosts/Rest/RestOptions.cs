namespace Basil.Host.Rest;

/// <summary>Operational settings of the REST and image hosts.</summary>
public sealed class RestOptions
{
	/// <summary>Gets the public domain the hosts answer on; its sub-domains are the host labels of the metrics.</summary>
	public string Domain { get; set; } = "localhost";
}
