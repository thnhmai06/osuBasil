using Basil.Gateway.Limits;

namespace Basil.Gateway.Admission;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdmissionPolicyAttribute : Attribute
{
	public WorkClass Class { get; init; } = WorkClass.Interactive;
}