using System.Diagnostics.Metrics;
using Basil.Application.Sessions.Objects;

namespace Basil.Infrastructure.Sessions.Metrics;

internal static class SessionsMeter
{
	internal static readonly Meter Instance = SessionMetrics.Meter;
}