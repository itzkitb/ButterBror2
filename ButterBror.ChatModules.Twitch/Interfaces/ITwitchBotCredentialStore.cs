using ButterBror.ChatModules.Twitch.Services;

namespace ButterBror.ChatModules.Twitch.Interfaces;

public interface ITwitchBotCredentialStore
{
    Task<TwitchBotCredential?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(TwitchBotCredential credential, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}