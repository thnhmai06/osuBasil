namespace Basil.Gateway;

/// <summary>Marks an entry point as having decided its admission policy, including a decision to apply none.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AdmissionPolicyAttribute : Attribute;
