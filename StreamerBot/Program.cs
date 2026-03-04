using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using StreamerBot;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddDiscordGateway(opts =>
    {
        opts.Intents = GatewayIntents.GuildVoiceStates | GatewayIntents.Guilds;
    })
    .AddGatewayHandlers(typeof(VoiceStateHandler).Assembly);

var host = builder.Build();
host.Run();
