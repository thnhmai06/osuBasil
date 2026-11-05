namespace Basil.Gateway;

/// <summary>How well the server is keeping up with the work asked of it.</summary>
public enum AdmissionHealth
{
	/// <summary>The server is keeping up and refuses nothing for load.</summary>
	Healthy,

	/// <summary>The server is busier than usual but still serves every kind of work.</summary>
	Degraded,

	/// <summary>The server is overloaded and refuses the least valuable work.</summary>
	Shedding
}