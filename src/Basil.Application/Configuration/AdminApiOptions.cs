namespace Basil.Application.Configuration;

/// <summary>
///     AdminKey moved to <see cref="ServerOptions.AdminKey"/> — the [Api] config section was removed.
///     This class is kept only to avoid breaking a DI registration that hasn't been cleaned up yet.
/// </summary>
[Obsolete("AdminKey is now on ServerOptions. Remove this class and its registration once confirmed.")]
public sealed class AdminApiOptions
{
    public const string SectionName = "Api";

    public string? AdminKey { get; init; }
}