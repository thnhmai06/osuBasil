using Bancho.Application.Configuration;
using Bancho.Application.Abstractions.Users;

namespace Bancho.Infrastructure.Persistence;

/// <summary>Shared by DI registration and the startup migration runner so both build the identical connection string.</summary>
public static class DatabaseConnectionStringBuilder
{
    public static string Build(DatabaseOptions options) =>
        $"Server={options.Host};Port={options.Port};User={options.User};Password={options.Password};Database={options.Name}";
}
