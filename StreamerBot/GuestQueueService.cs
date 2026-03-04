using System.Collections.Concurrent;

namespace StreamerBot;

public enum GuestQueueAddResult
{
    Added,
    AlreadyQueued,
    AlreadySpeaking,
    AdderLimitReached
}

public readonly record struct GuestQueueEntry(ulong GuestUserId, ulong AddedByUserId);

public readonly record struct GuestSpeakerSession(ulong GuildId, ulong ChannelId, ulong UserId, DateTimeOffset StartedAt);

public class GuestQueueService
{
    private readonly ConcurrentDictionary<ulong, GuildGuestState> _guildStates = new();

    public string GetGuests(ulong guildId)
    {
        var state = _guildStates.GetOrAdd(guildId, static _ => new GuildGuestState());
        return string.Join(", ", state.Queue.Select(entry => $"<@{entry.GuestUserId}>"));
    }
    
    public GuestQueueAddResult TryAddGuest(ulong guildId, ulong addedByUserId, ulong guestUserId)
    {
        var state = _guildStates.GetOrAdd(guildId, static _ => new GuildGuestState());

        lock (state.Sync)
        {
            if (state.ActiveSpeakers.ContainsKey(guestUserId))
                return GuestQueueAddResult.AlreadySpeaking;

            if (state.Queue.Any(entry => entry.GuestUserId == guestUserId))
                return GuestQueueAddResult.AlreadyQueued;

            var addedByCount = state.Queue.Count(entry => entry.AddedByUserId == addedByUserId);
            if (addedByCount >= 2)
                return GuestQueueAddResult.AdderLimitReached;

            state.Queue.AddLast(new GuestQueueEntry(guestUserId, addedByUserId));
            return GuestQueueAddResult.Added;
        }
    }

    public bool RemoveQueuedGuest(ulong guildId, ulong guestUserId)
    {
        if (!_guildStates.TryGetValue(guildId, out var state))
            return false;

        lock (state.Sync)
        {
            var node = state.Queue.First;
            while (node is not null)
            {
                if (node.Value.GuestUserId == guestUserId)
                {
                    state.Queue.Remove(node);
                    return true;
                }

                node = node.Next;
            }

            return false;
        }
    }

    public bool TryDequeueNextGuest(ulong guildId, out GuestQueueEntry entry)
    {
        entry = default;

        if (!_guildStates.TryGetValue(guildId, out var state))
            return false;

        lock (state.Sync)
        {
            if (state.Queue.First is null)
                return false;

            entry = state.Queue.First.Value;
            state.Queue.RemoveFirst();
            return true;
        }
    }

    public void MarkSpeakerStarted(ulong guildId, ulong channelId, ulong userId)
    {
        var state = _guildStates.GetOrAdd(guildId, static _ => new GuildGuestState());

        lock (state.Sync)
        {
            if (state.ActiveSpeakers.ContainsKey(userId))
                return;

            state.ActiveSpeakers[userId] = new GuestSpeakerSession(guildId, channelId, userId, DateTimeOffset.UtcNow);
        }
    }

    public bool TryGetSpeakerSession(ulong guildId, ulong userId, out GuestSpeakerSession session)
    {
        session = default;

        if (!_guildStates.TryGetValue(guildId, out var state))
            return false;

        lock (state.Sync)
        {
            return state.ActiveSpeakers.TryGetValue(userId, out session);
        }
    }

    public IReadOnlyList<GuestSpeakerSession> GetActiveSpeakers(ulong guildId)
    {
        if (!_guildStates.TryGetValue(guildId, out var state))
            return Array.Empty<GuestSpeakerSession>();

        lock (state.Sync)
        {
            return state.ActiveSpeakers.Values.ToArray();
        }
    }

    public void MarkSpeakerStopped(ulong guildId, ulong userId)
    {
        if (!_guildStates.TryGetValue(guildId, out var state))
            return;

        lock (state.Sync)
        {
            state.ActiveSpeakers.Remove(userId);
        }
    }

    public IReadOnlyList<GuestSpeakerSession> GetExpiredSpeakers(DateTimeOffset cutoff)
    {
        var expired = new List<GuestSpeakerSession>();

        foreach (var (_, state) in _guildStates)
        {
            lock (state.Sync)
            {
                expired.AddRange(state.ActiveSpeakers.Values.Where(session => session.StartedAt <= cutoff));
            }
        }

        return expired;
    }

    private sealed class GuildGuestState
    {
        public object Sync { get; } = new();

        public LinkedList<GuestQueueEntry> Queue { get; } = new();

        public Dictionary<ulong, GuestSpeakerSession> ActiveSpeakers { get; } = new();
    }
}
