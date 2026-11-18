namespace Basil.Application.Diagnostics.Services;

public static class HostLabels
{
	public const string Domain = "domain";

	public const string Other = "other";

	private static readonly string[] SubDomains = ["api", "a", "b", "osu", "c", "ce", "c4", "c5", "c6", "assets"];

	public static IReadOnlySet<string> All { get; } =
		new HashSet<string>([.. SubDomains, Domain, Other], StringComparer.Ordinal);

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