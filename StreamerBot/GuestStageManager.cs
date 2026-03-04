using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using Microsoft.Extensions.Options;
using System.Net;

namespace StreamerBot;

public class GuestStageManager(
    GatewayClient gatewayClient,
    RestClient restClient,
    GuestQueueService guestQueueService,
    IOptions<BotSettings> botSettings)
{
    private const int UnknownVoiceStateCode = 10065;
    private readonly BotSettings _botSettings = botSettings.Value;

    public async Task HandleVoiceStateUpdatedAsync(VoiceState newState)
    {
        if (!gatewayClient.Cache.Guilds.TryGetValue(newState.GuildId, out var guild))
            return;

        var previousTracked =
            guestQueueService.TryGetSpeakerSession(newState.GuildId, newState.UserId, out var trackedSession)
                ? trackedSession
                : (GuestSpeakerSession?)null;

        if (previousTracked is { } session)
        {
            var stillTrackedSpeaker = newState.ChannelId == session.ChannelId && !newState.Suppressed &&
                                      IsGuest(guild, newState.UserId);
            if (!stillTrackedSpeaker)
                guestQueueService.MarkSpeakerStopped(newState.GuildId, newState.UserId);
        }

        if (newState.ChannelId is { } channelId &&
            guild.Channels.TryGetValue(channelId, out var channel) &&
            channel is StageGuildChannel &&
            !newState.Suppressed &&
            IsGuest(guild, newState.UserId))
        {
            guestQueueService.MarkSpeakerStarted(newState.GuildId, channelId, newState.UserId);
        }

        var preferredChannelId = newState.ChannelId ?? previousTracked?.ChannelId;
        await EnsureGuestSpeakersAsync(newState.GuildId, preferredChannelId);
    }

    public async Task ProcessExpiredSpeakersAsync()
    {
        var expired = guestQueueService.GetExpiredSpeakers(DateTimeOffset.UtcNow.AddMinutes(-_botSettings.GuestTimeoutMinutes));

        foreach (var session in expired)
        {
            try
            {
                await restClient.ModifyGuildUserVoiceStateAsync(
                    session.GuildId,
                    session.ChannelId,
                    session.UserId,
                    options => options.WithSuppress());
            }
            catch (RestException ex) when (ex is
                                           {
                                               StatusCode: HttpStatusCode.NotFound, Error.Code: UnknownVoiceStateCode
                                           })
            {
                // State is already gone or out of date.
            }
            finally
            {
                guestQueueService.MarkSpeakerStopped(session.GuildId, session.UserId);
            }

            await EnsureGuestSpeakersAsync(session.GuildId, session.ChannelId);
        }
    }

    public async Task EnsureGuestSpeakersAsync(ulong guildId, ulong? preferredStageChannelId = null)
    {
        if (!gatewayClient.Cache.Guilds.TryGetValue(guildId, out var guild))
            return;

        ReconcileTrackedSpeakers(guildId, guild);

        var stageChannelId = ResolveStageChannelId(guild, preferredStageChannelId);
        if (stageChannelId is null)
            return;

        var channelId = stageChannelId.Value;

        var trackedGuestSessions = guild.VoiceStates.Values
            .Where(vs => vs.ChannelId == channelId && !vs.Suppressed && IsGuest(guild, vs.UserId))
            .Select(vs => vs.UserId)
            .ToArray();

        foreach (var guestUserId in trackedGuestSessions)
            guestQueueService.MarkSpeakerStarted(guildId, channelId, guestUserId);

        var activeGuestSpeakers = trackedGuestSessions.Length;

        while (activeGuestSpeakers < 2)
        {
            if (!guestQueueService.TryDequeueNextGuest(guildId, out var nextGuest))
                break;

            if (!IsGuest(guild, nextGuest.GuestUserId))
                continue;

            if (!guild.VoiceStates.TryGetValue(nextGuest.GuestUserId, out var guestVoiceState))
                continue;

            if (guestVoiceState.ChannelId != channelId)
                continue;

            if (!guestVoiceState.Suppressed)
            {
                guestQueueService.MarkSpeakerStarted(guildId, channelId, nextGuest.GuestUserId);
                activeGuestSpeakers++;
                continue;
            }

            try
            {
                await restClient.ModifyGuildUserVoiceStateAsync(
                    guildId,
                    channelId,
                    nextGuest.GuestUserId,
                    options => options.WithSuppress(false));

                guestQueueService.MarkSpeakerStarted(guildId, channelId, nextGuest.GuestUserId);
                activeGuestSpeakers++;
            }
            catch (RestException ex) when (ex is
                                           {
                                               StatusCode: HttpStatusCode.NotFound, Error.Code: UnknownVoiceStateCode
                                           })
            {
                // User state changed while processing the queue.
            }
        }
    }

    public void ReconcileGuestSpeaker(ulong guildId, ulong userId)
    {
        if (!gatewayClient.Cache.Guilds.TryGetValue(guildId, out var guild))
            return;

        if (!guild.VoiceStates.TryGetValue(userId, out var voiceState) ||
            voiceState.ChannelId is not { } channelId ||
            !guild.Channels.TryGetValue(channelId, out var channel) ||
            channel is not StageGuildChannel ||
            voiceState.Suppressed ||
            !IsGuest(guild, userId))
        {
            guestQueueService.MarkSpeakerStopped(guildId, userId);
            return;
        }

        guestQueueService.MarkSpeakerStarted(guildId, channelId, userId);
    }

    private void ReconcileTrackedSpeakers(ulong guildId, Guild guild)
    {
        var activeSessions = guestQueueService.GetActiveSpeakers(guildId);
        foreach (var session in activeSessions)
        {
            var userId = session.UserId;
            if (!guild.VoiceStates.TryGetValue(userId, out var voiceState) ||
                voiceState.ChannelId is not { } channelId ||
                !guild.Channels.TryGetValue(channelId, out var channel) ||
                channel is not StageGuildChannel ||
                voiceState.Suppressed ||
                !IsGuest(guild, userId))
            {
                guestQueueService.MarkSpeakerStopped(guildId, userId);
            }
        }
    }

    private ulong? ResolveStageChannelId(Guild guild, ulong? preferredStageChannelId)
    {
        if (preferredStageChannelId is { } preferredId &&
            guild.Channels.TryGetValue(preferredId, out var preferredChannel) &&
            preferredChannel is StageGuildChannel)
        {
            return preferredId;
        }

        var botUserId = gatewayClient.Cache.User?.Id;
        if (botUserId is not null &&
            guild.VoiceStates.TryGetValue(botUserId.Value, out var botState) &&
            botState.ChannelId is { } botChannelId &&
            guild.Channels.TryGetValue(botChannelId, out var botChannel) &&
            botChannel is StageGuildChannel)
        {
            return botChannelId;
        }

        var stageState = guild.VoiceStates.Values.FirstOrDefault(vs =>
            vs.ChannelId is { } channelId &&
            guild.Channels.TryGetValue(channelId, out var channel) &&
            channel is StageGuildChannel);

        return stageState?.ChannelId;
    }

    private bool IsGuest(Guild guild, ulong userId)
    {
        if (!guild.Users.TryGetValue(userId, out var guildUser))
            return true;

        return !guildUser.RoleIds.Contains(_botSettings.StreamerRoleId) &&
               !guildUser.RoleIds.Contains(_botSettings.ModRoleId);
    }
}
