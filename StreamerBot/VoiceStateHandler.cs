using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;

namespace StreamerBot;

public class VoiceStateHandler(GatewayClient gatewayClient, RestClient restClient) : IVoiceStateUpdateGatewayHandler
{
    private const ulong StreamerRoleId = 1286113718635204648;
    private const ulong ModRoleId = 1152657002212884550;

    public async ValueTask HandleAsync(VoiceState newState)
    {
        if (newState.ChannelId is null)
            return;

        if (!gatewayClient.Cache.Guilds.TryGetValue(newState.GuildId, out var guild))
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

        var botUser = gatewayClient.Cache.User;
        if (botUser is null)
            return;

        var botUserId = botUser.Id;
        var botInSameStage = guild.VoiceStates.TryGetValue(botUserId, out var botVoiceState) &&
                             botVoiceState.ChannelId == channelId;
        var botAlreadySelfMuted = botVoiceState?.IsSelfMuted == true;
        var botAlreadySelfDeafened = botVoiceState?.IsSelfDeafened == true;

        if (!botInSameStage || !botAlreadySelfMuted || !botAlreadySelfDeafened)
        {
            await gatewayClient.UpdateVoiceStateAsync(
                new VoiceStateProperties(newState.GuildId, channelId)
                    .WithSelfMute(true)
                    .WithSelfDeaf(true));
        }

        await restClient.ModifyCurrentGuildUserVoiceStateAsync(
            newState.GuildId,
            options => options
                .WithChannelId(channelId)
                .WithSuppress(false));
    }
}
