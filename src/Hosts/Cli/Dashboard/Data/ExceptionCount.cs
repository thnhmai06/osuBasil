namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>How many times an exception type was raised in the last minute.</summary>
/// <param name="Type">The name of the exception type.</param>
/// <param name="Count">How many times.</param>
public readonly record struct ExceptionCount(string Type, int Count);
