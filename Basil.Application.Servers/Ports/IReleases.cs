namespace Basil.Application.Servers.Ports;

/// <summary>The release of Basil that is installed and the newest one published.</summary>
public interface IReleases
{
	/// <summary>The version of the installed Basil, such as <c>1.2.3</c>.</summary>
	string Installed { get; }

	/// <summary>Gets the tag of the newest published release.</summary>
	/// <exception cref="HttpRequestException">The release feed could not be reached or refused the request.</exception>
	Task<string> LatestAsync(CancellationToken ct);
}