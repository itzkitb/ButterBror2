using System.Text.Json;
using ButterBror.ChatModules.Twitch.Interfaces;
using ButterBror.Data.Interfaces;

namespace ButterBror.ChatModules.Twitch.Services.Auth;

public sealed class TwitchBotCredentialStore(ICustomDataRepository repository) : ITwitchBotCredentialStore
{
    private const string RedisKey = "twitch:bot_token";

    public async Task<TwitchBotCredential?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await repository.GetDataAsync(RedisKey).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<TwitchBotCredential>(json);
    }

    public Task SaveAsync(TwitchBotCredential credential, CancellationToken cancellationToken = default) =>
        repository.SetDataAsync(RedisKey, JsonSerializer.Serialize(credential));

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        repository.DeleteDataAsync(RedisKey);
}