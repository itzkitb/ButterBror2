using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ButterBror.Core.Interfaces;
using ButterBror.Core.Models;
using ButterBror.Data.Interfaces;
using ButterBror.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class BanphraseService(
    IBanphraseRepository repository,
    ILogger<BanphraseService> logger)
    : IBanphraseService
{
    // ><> global categories
    private readonly ConcurrentDictionary<string, BanphraseCategory> _globalCategories = new();
    
    // ><> channel categories
    private readonly ConcurrentDictionary<string, BanphraseCategory> _channelCategories = new();
    private readonly int _maxChannelCategories = 1000;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    
    private bool _globalCategoriesLoaded;
    private readonly SemaphoreSlim _globalLoadSemaphore = new(1, 1);

    public async Task<BanphraseCheckResult> CheckMessageAsync(
        Guid chatId,
        string message,
        CancellationToken cancellationToken = default)
    {
        // s0: ensure global categories are loaded
        await EnsureGlobalCategoriesLoadedAsync();
        
        // s1: check global categories
        foreach (var category in _globalCategories.Values)
        {
            if (!category.IsMatch(message))
                continue;
            var matchedPhrase = category.GetMatchedPhrase(message);
            var matchedPattern = category.GetMatchedPatternPart(message);

            logger.LogDebug(
                "message blocked by global banphrase. Category: {Category}, Pattern: {Pattern}, Phrase: {Phrase}",
                category.CategoryName,
                matchedPattern,
                matchedPhrase);
                
            return new BanphraseCheckResult(
                false,
                category.CategoryName,
                "global",
                matchedPattern,
                matchedPhrase);
        }
        
        // s2: check channel-specific categories
        var channelCategories = await GetChannelCategoriesAsync(chatId);
        foreach (var category in channelCategories)
        {
            if (!category.IsMatch(message))
                continue;
            var matchedPhrase = category.GetMatchedPhrase(message);
            var matchedPattern = category.GetMatchedPatternPart(message);

            logger.LogDebug(
                "message blocked by channel banphrase. chat={ChatId}, cat={Category}, pattern={Pattern}, phrase={Phrase}",
                chatId,
                category.CategoryName,
                matchedPattern,
                matchedPhrase);

            return new BanphraseCheckResult(
                false,
                category.CategoryName,
                chatId.ToString(),
                matchedPattern,
                matchedPhrase);
        }
        
        return new BanphraseCheckResult(true);
    }

    private async Task EnsureGlobalCategoriesLoadedAsync()
    {
        if (_globalCategoriesLoaded)
        {
            return;
        }

        await _globalLoadSemaphore.WaitAsync();
        try
        {
            if (_globalCategoriesLoaded)
            {
                return;
            }

            await ReloadGlobalCategoriesAsync();
            _globalCategoriesLoaded = true;
        }
        finally
        {
            _globalLoadSemaphore.Release();
        }
    }

    public async Task ReloadGlobalCategoriesAsync()
    {
        var categories = await repository.GetGlobalCategoriesAsync();
        var newCategories = new ConcurrentDictionary<string, BanphraseCategory>();
        
        foreach (var kvp in categories)
        {
            var category = new BanphraseCategory
            {
                CategoryName = kvp.Name,
                Section = "global",
                ChatId = Guid.CreateVersion7(),
                RegexPattern = kvp.Pattern,
                LastAccessed = DateTime.UtcNow
            };
            category.CompileRegex();
            newCategories[kvp.Name] = category;
        }
        
        // atomic swap
        _globalCategories.Clear();
        foreach (var kvp in newCategories)
        {
            _globalCategories[kvp.Key] = kvp.Value;
        }
        
        logger.LogInformation(
            "loaded global banphrase categories. count={Count}",
            _globalCategories.Count);
    }

    private async Task<List<BanphraseCategory>> GetChannelCategoriesAsync(Guid chatId)
    {
        var result = new List<BanphraseCategory>();

        // get cached categories for this channel
        foreach (var category in _channelCategories.Values)
        {
            if (category.ChatId != chatId)
                continue;
            category.LastAccessed = DateTime.UtcNow;
            result.Add(category);
        }

        // if no cached categories, load from Redis
        if (result.Count != 0)
            return result;
        
        await LoadChannelCategoriesAsync(chatId);

        foreach (var category in _channelCategories.Values)
        {
            if (category.ChatId != chatId)
                continue;
            category.LastAccessed = DateTime.UtcNow;
            result.Add(category);
        }

        return result;
    }

    private async Task LoadChannelCategoriesAsync(Guid chatId)
    {
        await _cacheLock.WaitAsync();
        try
        {
            // Check if we need to evict old categories
            if (_channelCategories.Count >= _maxChannelCategories)
            {
                EvictOldestChannelCategories();
            }
            
            var categories = await repository.GetChannelCategoriesAsync(chatId);
            
            foreach (var kvp in categories)
            {
                var category = new BanphraseCategory
                {
                    CategoryName = kvp.Name,
                    Section = chatId.ToString(),
                    ChatId = chatId,
                    RegexPattern = kvp.Pattern,
                    LastAccessed = DateTime.UtcNow
                };
                category.CompileRegex();
                
                var key = $"{chatId}:{kvp.Name}";
                _channelCategories[key] = category;
            }
            
            logger.LogDebug(
                "loaded {Count} channel banphrase categories for {ChatId}",
                categories.Count,
                chatId);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private void EvictOldestChannelCategories()
    {
        var oldest = _channelCategories
            .OrderBy(c => c.Value.LastAccessed)
            .Take(_channelCategories.Count / 4)
            .ToList();
        
        foreach (var kvp in oldest)
        {
            _channelCategories.TryRemove(kvp.Key, out _);
        }
        
        logger.LogDebug("evicted {Count} old channel banphrase categories", oldest.Count);
    }

    public async Task<bool> SetCategoryAsync(
        string section,
        Guid chatId,
        string categoryName,
        string regexPattern)
    {
        try
        {
            // validate regex before saving
            _ = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
            
            if (section.Equals("global", StringComparison.InvariantCultureIgnoreCase))
            {
                await repository.SetGlobalCategoryAsync(categoryName, regexPattern);
                
                // update cache
                var category = new BanphraseCategory
                {
                    CategoryName = categoryName,
                    Section = "global",
                    RegexPattern = regexPattern
                };
                category.CompileRegex();
                _globalCategories[categoryName] = category;
                
                logger.LogInformation("set global banphrase category. cat={Category}", categoryName);
            }
            else
            {
                await repository.SetChannelCategoryAsync(chatId, categoryName, regexPattern);
                
                // update cache
                var category = new BanphraseCategory
                {
                    CategoryName = categoryName,
                    Section = chatId.ToString(),
                    ChatId = chatId,
                    RegexPattern = regexPattern
                };
                category.CompileRegex();
                var key = $"{chatId}:{categoryName}";
                _channelCategories[key] = category;
                
                logger.LogInformation(
                    "set channel banphrase category. cat={Category}, chat={ChatId}",
                    categoryName,
                    chatId);
            }
            
            return true;
        }
        catch (RegexParseException ex)
        {
            logger.LogError(ex, "invalid regex pattern for category: {Category}", categoryName);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "failed to set banphrase category: {Category}", categoryName);
            return false;
        }
    }

    public async Task<BanphraseRecord?> GetCategoryAsync(
        string section,
        Guid chatId,
        string categoryName)
    {
        if (section.Equals("global", StringComparison.InvariantCultureIgnoreCase))
        {
            return await repository.GetGlobalCategoryAsync(categoryName);
        }

        return await repository.GetChannelCategoryAsync(chatId, categoryName);
    }

    public async Task<IReadOnlyList<BanphraseRecord>> ListCategoriesAsync(
        string section,
        Guid chatId)
    {
        if (section.Equals("global", StringComparison.InvariantCultureIgnoreCase))
        {
            return await repository.GetGlobalCategoriesAsync();
        }

        return await repository.GetChannelCategoriesAsync(chatId);
    }

    public async Task<bool> DeleteCategoryAsync(
        string section,
        Guid chatId,
        string categoryName)
    {
        if (section.Equals("global", StringComparison.InvariantCultureIgnoreCase))
        {
            await repository.DeleteGlobalCategoryAsync(categoryName);
            _globalCategories.TryRemove(categoryName, out _);
            logger.LogInformation("deleted global banphrase category: {Category}", categoryName);
        }
        else
        {
            await repository.DeleteChannelCategoryAsync(chatId, categoryName);
            var key = $"{chatId}:{categoryName}";
            _channelCategories.TryRemove(key, out _);
            logger.LogInformation(
                "deleted channel banphrase category: {Category} for {ChatId}",
                categoryName,
                chatId);
        }
        
        return true;
    }
}