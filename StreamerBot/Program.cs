using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Services.ApplicationCommands;
using Microsoft.Extensions.Options;
using StreamerBot;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<BotSettings>()
    .Bind(builder.Configuration.GetSection(BotSettings.SectionName));

builder.Services.AddSingleton<GuestQueueService>();
builder.Services.AddSingleton<GuestStageManager>();
builder.Services.AddHostedService<GuestSpeakerRotationService>();

builder.Services
    .AddDiscordGateway(opts =>
    {
        opts.Intents = GatewayIntents.GuildVoiceStates |
                       GatewayIntents.Guilds |
                       GatewayIntents.GuildUsers;
    })
    .AddGatewayHandlers(typeof(VoiceStateHandler).Assembly)
    .AddApplicationCommands<SlashCommandInteraction, SlashCommandContext>(opts =>
    {
        opts.AutoRegisterCommands = true;
        opts.ResultHandler = new ApplicationCommandResultHandler<SlashCommandContext>(MessageFlags.Ephemeral);
    });

var host = builder.Build();

host.AddModules(typeof(StreamerCommandModule).Assembly);

host.Run();
