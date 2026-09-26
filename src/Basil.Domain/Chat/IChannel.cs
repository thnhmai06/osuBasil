using Basil.Domain.Client;

namespace Basil.Domain.Chat;

public interface IChannel : IEquatable<IChannel>
{
	string Name { get; }

	/// <summary>The channel topic shown to joining users.</summary>
	string Topic { get; }

	string DisplayName { get; }

	/// <summary>The minimum privilege required to read the channel.</summary>
	ClientPrivileges ReadPrivilege { get; }

	/// <summary>The minimum privilege required to write to the channel.</summary>
	ClientPrivileges WritePrivilege { get; }

	/// <summary>A value that indicates whether the channel is joined automatically at login.</summary>
	bool AutoJoin { get; }

	bool Visible { get; }
}