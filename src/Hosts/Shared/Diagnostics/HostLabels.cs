namespace Basil.Host.Shared.Diagnostics;

/// <summary>Turns the Host header of a request into one of a fixed set of metric labels, so no caller can create new ones.</summary>
public static class HostLabels
{
	/// <summary>The label of the bare domain.</summary>
	public const string Domain = "domain";

	/// <summary>The label of every other host name.</summary>
	public const string Other = "other";

	/// <summary>The HTTP sub-domains the hosts answer on (the Bancho, web, API and image hosts).</summary>
	private static readonly string[] SubDomains = ["api", "a", "b", "osu", "c", "ce", "c4", "c5", "c6", "assets"];

	/// <summary>Gets every label that <see cref="Resolve" /> can return.</summary>
	public static IReadOnlySet<string> All { get; } =
		new HashSet<string>([.. SubDomains, Domain, Other], StringComparer.Ordinal);

	/// <summary>Chooses the label of a request.</summary>
	/// <param name="host">The host name the request was sent to.</param>
	/// <param name="domain">The configured public domain.</param>
	/// <returns>
	///     The sub-domain when <paramref name="host" /> is a known sub-domain of <paramref name="domain" />,
	///     <see cref="Domain" /> for the domain itself, otherwise <see cref="Other" />.
	/// </returns>
	public static string Resolve(string? host, string domain)
	{
		if (string.IsNullOrEmpty(host)) return Other;
		if (host.Equals(domain, StringComparison.OrdinalIgnoreCase)) return Domain;

		foreach (var sub in SubDomains)
			if (host.Length == sub.Length + 1 + domain.Length &&
			    host.StartsWith(sub, StringComparison.OrdinalIgnoreCase) &&
			    host[sub.Length] == '.' && host.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
				return sub;

		return Other;
	}
}