using Mediator;

namespace Basil.Application.Shared.Settings;

/// <summary>Asks Content for the settings in force, for the checks that admit a request.</summary>
/// <remarks>The settings that admit requests are public: the question carries no actor.</remarks>
public sealed record ReadSettings : IRequest<ServerSettings>;