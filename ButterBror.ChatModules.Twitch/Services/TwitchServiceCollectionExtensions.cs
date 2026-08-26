using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.ChatModules.Twitch.Models;
using ButterBror.ChatModules.Twitch.Services.Auth;
using ButterBror.ChatModules.Twitch.Services.ChatTransports;
using ButterBror.Data.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using TwitchLib.Api;
using TwitchLib.EventSub.Websockets;

namespace ButterBror.ChatModules.Twitch.Services;

public static class TwitchServiceCollectionExtensions
{
    public static IServiceCollection AddTwitchChatTransports(
        this IServiceCollection services,
        Action<TwitchConfiguration> configure)
    {
        services.AddOptions<TwitchConfiguration>().Configure(configure);
        services.AddHttpClient("twitch-token", client =>
            client.BaseAddress = new Uri("https://id.twitch.tv/"));
        services.AddHttpClient("twitch-auth-api");
        services.AddSingleton<ITwitchTokenManager, TwitchTokenManager>();
        services.AddSingleton<TwitchTokenRefreshBackgroundService>();
        services.AddSingleton<ITwitchBotCredentialStore, TwitchBotCredentialStore>();
        services.AddSingleton<TwitchAuthFileWatcher>();
        services.AddSingleton<TwitchAPI>();
        services.AddSingleton<EventSubWebsocketClient>();
        services.AddSingleton<EventSubChatTransport>();
        services.AddSingleton<IrcChatTransport>();
        services.AddSingleton<ITwitchChatTransport, TwitchChatTransportStrategy>();
        return services;
    }
}