using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ButterBror.Core.Interfaces;
using ButterBror.Data;
using ButterBror.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class BanphraseService : IBanphraseService
{
    private readonly IBanphraseRepository _repository;
    private readonly ILogger<BanphraseService> _logger;
    
    // Global categories - always loaded
    private readonly ConcurrentDictionary<string, BanphraseCategory> _globalCategories = new();
    
    // Channel categories - LRU cached with limit
    private readonly ConcurrentDictionary<string, BanphraseCategory> _channelCategories = new();
    private readonly int _maxChannelCategories = 1000; // Limit to prevent memory overflow
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    
    private bool _globalCategoriesLoaded = false;
    private readonly object _globalLoadLock = new();

    public BanphraseService(
        IBanphraseRepository repository,
        ILogger<BanphraseService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<BanphraseCheckResult> CheckMessageAsync(
        string channelId,
        string platform,
        string message,
        CancellationToken cancellationToken = default)
    {
        // Ensure global categories are loaded
        await EnsureGlobalCategoriesLoadedAsync();
        
        // Check global categories
        foreach (var category in _globalCategories.Values)
        {
            if (category.IsMatch(message))
            {
                var matchedPhrase = category.GetMatchedPhrase(message);
                var matchedPattern = category.GetMatchedPatternPart(message);

                _logger.LogDebug(
                    "Message blocked by global banphrase. Category: {Category}, Pattern: {Pattern}, Phrase: {Phrase}",
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
        }
        
        // Check channel-specific categories
        var channelCategories = await GetChannelCategoriesAsync(platform, channelId);
        foreach (var category in channelCategories)
        {
            if (category.IsMatch(message))
            {
                var matchedPhrase = category.GetMatchedPhrase(message);
                var matchedPattern = category.GetMatchedPatternPart(message);

                _logger.LogDebug(
                    "Message blocked by channel banphrase. Channel: {Channel}, Category: {Category}, Pattern: {Pattern}, Phrase: {Phrase}",
                    channelId,
                    category.CategoryName,
                    matchedPattern,
                    matchedPhrase);

                return new BanphraseCheckResult(
                    false,
                    category.CategoryName,
                    $"{platform}:{channelId}",
                    matchedPattern,
                    matchedPhrase);
            }
        }
        
        return new BanphraseCheckResult(true);
    }

    private async Task EnsureGlobalCategoriesLoadedAsync()
    {
        if (_globalCategoriesLoaded)
        {
            return;
        }
        
        lock (_globalLoadLock)
        {
            if (_globalCategoriesLoaded)
            {
                return;
            }
            
            _globalCategoriesLoaded = true;
        }
        
        await ReloadGlobalCategoriesAsync();
    }

    public async Task ReloadGlobalCategoriesAsync()
    {
        _logger.LogInformation("Reloading global banphrase categories...");
        
        var categories = await _repository.GetAllGlobalCategoriesAsync();
        var newCategories = new ConcurrentDictionary<string, BanphraseCategory>();
        
        foreach (var kvp in categories)
        {
            var category = new BanphraseCategory
            {
                CategoryName = kvp.Key,
                Section = "global",
                Platform = "",
                ChannelId = "",
                RegexPattern = kvp.Value,
                LastAccessed = DateTime.UtcNow
            };
            category.CompileRegex();
            newCategories[kvp.Key] = category;
        }
        
        // Atomic swap
        _globalCategories.Clear();
        foreach (var kvp in newCategories)
        {
            _globalCategories[kvp.Key] = kvp.Value;
        }
        
        _logger.LogInformation(
            "Loaded {Count} global banphrase categories",
            _globalCategories.Count);
    }

    private async Task<List<BanphraseCategory>> GetChannelCategoriesAsync(string platform, string channelId)
    {
        var result = new List<BanphraseCategory>();
        var channelKey = $"{platform}:{channelId}";
        
        // Get cached categories for this channel
        foreach (var category in _channelCategories.Values)
        {
            if (category.Platform == platform && category.ChannelId == channelId)
            {
                category.LastAccessed = DateTime.UtcNow;
                result.Add(category);
            }
        }
        
        // If no cached categories, load from Redis
        if (result.Count == 0)
        {
            await LoadChannelCategoriesAsync(platform, channelId);
            
            foreach (var category in _channelCategories.Values)
            {
                if (category.Platform == platform && category.ChannelId == channelId)
                {
                    category.LastAccessed = DateTime.UtcNow;
                    result.Add(category);
                }
            }
        }
        
        return result;
    }

    private async Task LoadChannelCategoriesAsync(string platform, string channelId)
    {
        await _cacheLock.WaitAsync();
        try
        {
            // Check if we need to evict old categories
            if (_channelCategories.Count >= _maxChannelCategories)
            {
                EvictOldestChannelCategories();
            }
            
            var categories = await _repository.GetAllChannelCategoriesAsync(platform, channelId);
            
            foreach (var kvp in categories)
            {
                var category = new BanphraseCategory
                {
                    CategoryName = kvp.Key,
                    Section = $"{platform}:{channelId}",
                    Platform = platform,
                    ChannelId = channelId,
                    RegexPattern = kvp.Value,
                    LastAccessed = DateTime.UtcNow
                };
                category.CompileRegex();
                
                var key = $"{platform}:{channelId}:{kvp.Key}";
                _channelCategories[key] = category;
            }
            
            _logger.LogDebug(
                "Loaded {Count} channel banphrase categories for {Platform}:{ChannelId}",
                categories.Count,
                platform,
                channelId);
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
            .Take(_channelCategories.Count / 4) // Evict 25% of oldest
            .ToList();
        
        foreach (var kvp in oldest)
        {
            _channelCategories.TryRemove(kvp.Key, out _);
        }
        
        _logger.LogDebug("Evicted {Count} old channel banphrase categories", oldest.Count);
    }

    public async Task<bool> SetCategoryAsync(
        string section,
        string platform,
        string channelId,
        string categoryName,
        string regexPattern)
    {
        try
        {
            // Validate regex before saving
            _ = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
            
            if (section.ToLowerInvariant() == "global")
            {
                await _repository.SetGlobalCategoryAsync(categoryName, regexPattern);
                
                // Update cache
                var category = new BanphraseCategory
                {
                    CategoryName = categoryName,
                    Section = "global",
                    RegexPattern = regexPattern
                };
                category.CompileRegex();
                _globalCategories[categoryName] = category;
                
                _logger.LogInformation("Set global banphrase category: {Category}", categoryName);
            }
            else
            {
                await _repository.SetChannelCategoryAsync(platform, channelId, categoryName, regexPattern);
                
                // Update cache
                var category = new BanphraseCategory
                {
                    CategoryName = categoryName,
                    Section = $"{platform}:{channelId}",
                    Platform = platform,
                    ChannelId = channelId,
                    RegexPattern = regexPattern
                };
                category.CompileRegex();
                var key = $"{platform}:{channelId}:{categoryName}";
                _channelCategories[key] = category;
                
                _logger.LogInformation(
                    "Set channel banphrase category: {Category} for {Platform}:{ChannelId}",
                    categoryName,
                    platform,
                    channelId);
            }
            
            return true;
        }
        catch (RegexParseException ex)
        {
            _logger.LogError(ex, "Invalid regex pattern for category: {Category}", categoryName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set banphrase category: {Category}", categoryName);
            return false;
        }
    }

    public async Task<string?> GetCategoryAsync(
        string section,
        string platform,
        string channelId,
        string categoryName)
    {
        if (section.ToLowerInvariant() == "global")
        {
            return await _repository.GetGlobalCategoryAsync(categoryName);
        }
        else
        {
            return await _repository.GetChannelCategoryAsync(platform, channelId, categoryName);
        }
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(
        string section,
        string platform,
        string channelId)
    {
        if (section.ToLowerInvariant() == "global")
        {
            return await _repository.GetGlobalCategoryNamesAsync();
        }
        else
        {
            return await _repository.GetChannelCategoryNamesAsync(platform, channelId);
        }
    }

    public async Task<bool> DeleteCategoryAsync(
        string section,
        string platform,
        string channelId,
        string categoryName)
    {
        if (section.ToLowerInvariant() == "global")
        {
            await _repository.DeleteGlobalCategoryAsync(categoryName);
            _globalCategories.TryRemove(categoryName, out _);
            _logger.LogInformation("Deleted global banphrase category: {Category}", categoryName);
        }
        else
        {
            await _repository.DeleteChannelCategoryAsync(platform, channelId, categoryName);
            var key = $"{platform}:{channelId}:{categoryName}";
            _channelCategories.TryRemove(key, out _);
            _logger.LogInformation(
                "Deleted channel banphrase category: {Category} for {Platform}:{ChannelId}",
                categoryName,
                platform,
                channelId);
        }
        
        return true;
    }
}