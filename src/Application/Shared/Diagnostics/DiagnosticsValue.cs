namespace Basil.Application.Shared.Diagnostics;

public readonly record struct DiagnosticsValue(string Category, string Name, long Value);