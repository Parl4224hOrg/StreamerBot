using NetCord;
using NetCord.Gateway;
using NetCord.Services.ApplicationCommands;

namespace StreamerBot;

[SlashCommand("streamer", "Streamer-related commands")]
public class StreamerCommandModule : CommandModuleBase
{
    [SubSlashCommand("guest", "Manage guests")]
    public class GuestCommandModule(
        GuestQueueService guestQueueService,
        GuestStageManager guestStageManager
    ) : CommandModuleBase
    {
        private async Task<Guild?> EnsureInvokerAuthorizedAsync()
        {
            var guild = Context.Guild;
            if (guild is null)
            {
                await ReplyAsync("Guild not found.", true);
                return null;
            }

            guild.Users.TryGetValue(Context.User.Id, out var invoker);
            invoker ??= await guild.GetUserAsync(Context.User.Id);

            var isAuthorized = invoker.RoleIds.Contains(RoleConstants.ModRoleId) ||
                               invoker.RoleIds.Contains(RoleConstants.StreamerRoleId);
            if (isAuthorized)
                return guild;

            await ReplyAsync("You must have the mod or streamer role to use this command.", true);
            return null;
        }

        [SubSlashCommand("add", "Add a guest to the queue")]
        public async Task AddGuest(
            [SlashCommandParameter(Name = "guest", Description = "The user to add to the queue")]
            User user
        )
        {
            var guild = await EnsureInvokerAuthorizedAsync();
            if (guild is null)
                return;

            guild.Users.TryGetValue(user.Id, out var targetUser);
            targetUser ??= await guild.GetUserAsync(user.Id);

            var isModOrStreamer = targetUser.RoleIds.Contains(RoleConstants.ModRoleId) ||
                                  targetUser.RoleIds.Contains(RoleConstants.StreamerRoleId);
            if (isModOrStreamer)
            {
                await ReplyAsync($"User {user.Username} is a mod or streamer and cannot be added as a guest.", true);
                return;
            }

            guestStageManager.ReconcileGuestSpeaker(guild.Id, user.Id);

            var addResult = guestQueueService.TryAddGuest(guild.Id, Context.User.Id, user.Id);
            switch (addResult)
            {
                case GuestQueueAddResult.Added:
                    await guestStageManager.EnsureGuestSpeakersAsync(guild.Id);
                    await ReplyAsync($"Guest {user.Username} added to the queue.", true);
                    return;
                case GuestQueueAddResult.AdderLimitReached:
                    await ReplyAsync("You already have two guests in the queue.", true);
                    return;
                case GuestQueueAddResult.AlreadyQueued:
                    await ReplyAsync("The guest is already in the queue.", true);
                    return;
                case GuestQueueAddResult.AlreadySpeaking:
                    await ReplyAsync("The guest is already speaking.", true);
                    return;
            }
        }

        [SubSlashCommand("remove", "Remove a guest from the queue")]
        public async Task RemoveGuest(
            [SlashCommandParameter(Name = "guest", Description = "The user to add to the queue")]
            User user
        )
        {
            var guild = await EnsureInvokerAuthorizedAsync();
            if (guild is null)
                return;

            var removed = guestQueueService.RemoveQueuedGuest(guild.Id, user.Id);
            if (removed)
            {
                await ReplyAsync($"Guest {user.Username} removed from the queue.", true);
                return;
            }

            await ReplyAsync("That guest is not currently in the queue.", true);
        }

        [SubSlashCommand("list", "List all guests in the queue")]
        public async Task ListGuests()
        {
            var guild = await EnsureInvokerAuthorizedAsync();
            if (guild is null)
                return;

            var guests = guestQueueService.GetGuests(guild.Id);
            await ReplyAsync($"Guests in queue:\n{guests}", true);
        }
    }
}
