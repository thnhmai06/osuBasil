using Basil.Application.Common.Settings;
using Mediator;

namespace Basil.Application.Common.Settings;

/// <summary>Asks Content for the settings in force, for the checks that admit a request.</summary>
/// <remarks>The settings that admit requests are public: the question carries no actor.</remarks>
public sealed record ReadSettings : IRequest<ServerSettings>;