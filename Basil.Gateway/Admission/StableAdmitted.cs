using Basil.Application.Sessions.Objects;
using Basil.Gateway.Limits;

namespace Basil.Gateway.Admission;

/// <summary>The answer to a request of the osu! client: the decision, the kind of work it was counted as and the session its token names, which the request releases when it ends.</summary>
public readonly record struct StableAdmitted(AdmissionDecision Decision, WorkClass Class, Session? Session);