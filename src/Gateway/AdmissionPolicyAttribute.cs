namespace Basil.Gateway;

/// <summary>Marks an entry point as having decided its admission policy, including a decision to apply none.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdmissionPolicyAttribute : Attribute
{
	/// <summary>Gets the kind of work the entry point does; it chooses the concurrency cap and the place in the shed order.</summary>
	public WorkClass Class { get; init; } = WorkClass.Interactive;
}