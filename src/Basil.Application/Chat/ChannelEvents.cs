using Basil.Application.Common.Events;
using Basil.Application.Sessions;

namespace Basil.Application.Chat;

/// <summary>Something happened to a channel session.</summary>
public abstract record ChannelEvent(ChannelSession Channel) : Event;

/// <summary>A connection joined or parted a channel.</summary>
public abstract record ChannelMembershipEvent(ChannelSession Channel) : ChannelEvent(Channel);

/// <summary>A connection joined a channel.</summary>
public sealed record MemberJoined(ChannelSession Channel, Connection Member) : ChannelMembershipEvent(Channel);

/// <summary>A connection parted a channel.</summary>
public sealed record MemberParted(ChannelSession Channel, Connection Member) : ChannelMembershipEvent(Channel);