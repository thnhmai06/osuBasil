namespace Basil.LoadTests.Configuration;

/// <summary>The pool of accounts seeded before a run and shared across every scenario.</summary>
public sealed class AccountPoolSettings
{
	/// <summary>How many accounts to seed. Must be at least the largest concurrency any scenario asks for.</summary>
	public int Count { get; init; } = 100;

	/// <summary>Prefix for generated usernames (<c>{NamePrefix}0001</c>, <c>{NamePrefix}0002</c>, ...).</summary>
	public string NamePrefix { get; init; } = "load";

	/// <summary>The plaintext password every seeded account shares.</summary>
	public string Password { get; init; } = "loadtest-pw";
}