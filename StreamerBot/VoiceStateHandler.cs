using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;
using System.Net;

namespace StreamerBot;

public class VoiceStateHandler(GatewayClient gatewayClient, RestClient restClient) : IVoiceStateUpdateGatewayHandler
{
    private const ulong StreamerRoleId = 1286113718635204648;
    private const ulong ModRoleId = 1152657002212884550;
    private const int UnknownVoiceStateCode = 10065;

    public async ValueTask HandleAsync(VoiceState newState)
    {
        if (!gatewayClient.Cache.Guilds.TryGetValue(newState.GuildId, out var guild))
            return;

        var botUser = gatewayClient.Cache.User;
        if (botUser is null)
            return;

        var botUserId = botUser.Id;
        if (guild.VoiceStates.TryGetValue(botUserId, out var botState) && botState.ChannelId is { } botChannelId)
        {
            var hasOtherSpeakers = guild.VoiceStates.Values.Any(vs =>
            {
                if (vs.UserId == botUserId)
                    return false;

                // Use the incoming event state for the updated user to avoid stale-cache behavior on leave events.
                if (vs.UserId == newState.UserId)
                    return newState.ChannelId == botChannelId && !newState.Suppressed;

                return vs.ChannelId == botChannelId && !vs.Suppressed;
            });

            if (!hasOtherSpeakers)
            {
                await gatewayClient.UpdateVoiceStateAsync(new VoiceStateProperties(newState.GuildId, null));
                return;
            }
        }

        if (newState.ChannelId is null)
            return;

        var channelId = newState.ChannelId.Value;
        if (!guild.Channels.TryGetValue(channelId, out var channel) || channel is not StageGuildChannel)
            return;

        if (!guild.Users.TryGetValue(newState.UserId, out var guildUser))
            return;

        var isStreamer = guildUser.RoleIds.Contains(StreamerRoleId);
        var isMod = guildUser.RoleIds.Contains(ModRoleId);
        if (!isStreamer && !isMod)
            return;

        if (newState.Suppressed)
        {
            await restClient.ModifyGuildUserVoiceStateAsync(
                newState.GuildId,
                channelId,
                newState.UserId,
                options => options.WithSuppress(false));
        }

        var usersInStage = guild.VoiceStates.Values.Count(vs => vs.ChannelId == channelId);
        var stageInstance = guild.StageInstances.Values.FirstOrDefault(si => si.ChannelId == channelId);
        var hasCurrentEvent = stageInstance is not null && !string.IsNullOrWhiteSpace(stageInstance.Topic);

        if (usersInStage <= 1 || !hasCurrentEvent)
        {
            var topic = $"{guildUser.Username}'s Match Stream";

            if (stageInstance is null)
            {
                await restClient.CreateStageInstanceAsync(new StageInstanceProperties(channelId, topic));
            }
            else
            {
                await restClient.ModifyStageInstanceAsync(channelId, options => options.WithTopic(topic));
            }
        }

        var botInSameStage = guild.VoiceStates.TryGetValue(botUserId, out var botVoiceState) &&
                             botVoiceState.ChannelId == channelId;
        var botAlreadySelfMuted = botVoiceState?.IsSelfMuted == true;
        var botAlreadySelfDeafened = botVoiceState?.IsSelfDeafened == true;

        if (!botInSameStage || !botAlreadySelfMuted || !botAlreadySelfDeafened)
        {
            await gatewayClient.UpdateVoiceStateAsync(
                new VoiceStateProperties(newState.GuildId, channelId)
                    .WithSelfMute()
                    .WithSelfDeaf());
        }

        var botIsSuppressedInStage = botInSameStage && botVoiceState?.Suppressed == true;
        if (!botIsSuppressedInStage)
            return;

        try
        {
            await restClient.ModifyCurrentGuildUserVoiceStateAsync(
                newState.GuildId,
                options => options
                    .WithChannelId(channelId)
                    .WithSuppress(false));
        }
        catch (RestException ex) when (ex is { StatusCode: HttpStatusCode.NotFound, Error.Code: UnknownVoiceStateCode })
        {
            // Discord can return Unknown Voice State briefly while the bot join state propagates.
        }
    }
}
