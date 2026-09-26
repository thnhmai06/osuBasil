using Basil.Application.Contracts.Registries;
using Basil.Application.Models;
using Basil.Application.Models.Notifications;

namespace Basil.Application.Services.Operations;

/// <summary>Runs a room's <c>!mp start</c>/<c>!mp timer</c> countdown, ticking milestones to its players.</summary>
public sealed class RoomCountdowns(IRoomRegistry rooms, IPlayerRegistry players, TimeProvider timeProvider)
{
	private static readonly TimeSpan[] TickMarks =
		[TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5)];

	private readonly Dictionary<int, Countdown> _countdowns = new();
	private readonly Lock _sync = new();

	/// <summary>Starts (replacing any running) countdown for a room.</summary>
	/// <param name="roomId">The id of the room the countdown runs for.</param>
	/// <param name="length">How long the countdown runs.</param>
	/// <param name="startsMatch">
	///     Whether the room's round is started automatically when the countdown reaches zero
	///     (<c>!mp start</c>), as opposed to only announcing the elapsed time (<c>!mp timer</c>).
	/// </param>
	public void Start(int roomId, TimeSpan length, bool startsMatch)
	{
		var countdown = new Countdown(length, BuildMilestones(roomId, startsMatch), timeProvider);

		lock (_sync)
		{
			if (_countdowns.Remove(roomId, out var existing))
				existing.Dispose();
			_countdowns[roomId] = countdown;
		}

		countdown.Start();
	}

	/// <summary>Cancels a room's running countdown, if any.</summary>
	/// <param name="roomId">The id of the room whose countdown to cancel.</param>
	public void Cancel(int roomId)
	{
		Countdown? countdown;
		lock (_sync)
		{
			if (!_countdowns.Remove(roomId, out countdown)) return;
		}

		countdown.Dispose();
	}

	private IEnumerable<Countdown.Milestone> BuildMilestones(int roomId, bool startsMatch)
	{
		foreach (var mark in TickMarks)
		{
			var remaining = mark;
			yield return new Countdown.Milestone(remaining, _ =>
			{
				NotifyRoom(roomId, new CountdownTick(remaining));
				return Task.CompletedTask;
			});
		}

		yield return new Countdown.Milestone(TimeSpan.Zero, async _ =>
		{
			lock (_sync)
			{
				_countdowns.Remove(roomId);
			}

			if (startsMatch)
				await StartMatchAsync(roomId);
			else
				NotifyRoom(roomId, new CountdownTick(TimeSpan.Zero));
		});
	}

	private async Task StartMatchAsync(int roomId)
	{
		if (await rooms.EnterAsync(roomId) is not { } scope) return;
		await using (scope)
		{
			if (!scope.Room.InProgress)
				scope.Room.Start();
		}
	}

	private void NotifyRoom(int roomId, Notification notification)
	{
		if (!rooms.AllById.TryGetValue(roomId, out var room)) return;

		foreach (var slot in room.Slots)
			if (slot.User is { } user && players.AllById.TryGetValue(user.Id, out var session))
				session.Notify(notification);
	}
}