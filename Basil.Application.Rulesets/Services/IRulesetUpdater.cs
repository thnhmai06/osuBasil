namespace Basil.Application.Rulesets.Services;

/// <summary>Whether the osu! calculation stack the server was built against is available.</summary>
public enum RulesetState : byte
{
	/// <summary>Every file of the stack is present and intact; star ratings, pp and beatmapset imports work.</summary>
	Installed,

	/// <summary>The stack is not there; star ratings and pp stay empty and imports are refused until it is installed.</summary>
	Missing,

	/// <summary>A file of the stack is missing or does not match what the server expects, so the stack is not used.</summary>
	Damaged
}

/// <summary>The version of the calculation stack the server needs and whether it is available.</summary>
public sealed record RulesetVersions(string Version, RulesetState State);

/// <summary>What an installation did: <paramref name="Downloaded"/> is false when the stack was installed already.</summary>
public sealed record RulesetInstallResult(string Version, bool Downloaded, bool RestartRequired);

/// <summary>Reports and installs the osu! calculation stack.</summary>
public interface IRulesetUpdater
{
	RulesetVersions Version();

	/// <summary>Downloads and verifies the missing or damaged stack; null when another installation is running.</summary>
	/// <exception cref="RulesetUpdateException">The download or its verification failed; nothing is kept.</exception>
	Task<RulesetInstallResult?> InstallAsync(CancellationToken ct = default);
}

public sealed class RulesetUpdateException(string message, Exception? inner = null) : Exception(message, inner);
