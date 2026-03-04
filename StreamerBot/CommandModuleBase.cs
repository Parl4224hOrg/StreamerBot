using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace StreamerBot;

public class CommandModuleBase : ApplicationCommandModule<SlashCommandContext>
{
    /// <summary>
    ///     Sends an interaction response message.
    /// </summary>
    /// <param name="message">Message content.</param>
    /// <param name="ephemeral">Whether the response should be ephemeral.</param>
    protected async Task ReplyAsync(string message, bool ephemeral = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.Message(new InteractionMessageProperties
            {
                Content = message,
                Flags = ephemeral ? MessageFlags.Ephemeral : null
            }));
    }

    /// <summary>
    ///     Defers the interaction response.
    /// </summary>
    /// <param name="ephemeral">Whether the deferred response should be ephemeral.</param>
    protected async Task DeferAsync(bool ephemeral = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(ephemeral ? MessageFlags.Ephemeral : null));
    }

    /// <summary>
    ///     Sends a follow-up interaction message.
    /// </summary>
    /// <param name="message">Message content.</param>
    /// <param name="ephemeral">Whether the follow-up should be ephemeral.</param>
    protected async Task FollowupAsync(string message, bool ephemeral = false)
    {
        await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties
        {
            Content = message,
            Flags = ephemeral ? MessageFlags.Ephemeral : null
        });
    }
}