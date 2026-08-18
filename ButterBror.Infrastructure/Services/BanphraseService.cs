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
        Guid chatId,
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
        var channelCategories = await GetChannelCategoriesAsync(chatId);
        foreach (var category in channelCategories)
        {
            if (category.IsMatch(message))
            {
                var matchedPhrase = category.GetMatchedPhrase(message);
                var matchedPattern = category.GetMatchedPatternPart(message);

                _logger.LogDebug(
                    "message blocked by channel banphrase. chat: {Channel}, cat: {Category}, pattern: {Pattern}, phrase: {Phrase}",
                    chatId,
                    category.CategoryName,
                    matchedPattern,
                    matchedPhrase);

                return new BanphraseCheckResult(
                    false,
                    category.CategoryName,
                    $"{chatId}",
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
        var categories = await _repository.GetAllGlobalCategoriesAsync();
        var newCategories = new ConcurrentDictionary<string, BanphraseCategory>();
        
        foreach (var kvp in categories)
        {
            var category = new BanphraseCategory
            {
                CategoryName = kvp.Key,
                Section = "global",
                ChatId = new Guid(),
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
            "Loaded global banphrase categories. count={Count}",
            _globalCategories.Count);
    }

    private async Task<List<BanphraseCategory>> GetChannelCategoriesAsync(Guid chatId)
    {
        var result = new List<BanphraseCategory>();
        var channelKey = chatId.ToString();
        
        // Get cached categories for this channel
        foreach (var category in _channelCategories.Values)
        {
            if (category.ChatId == chatId)
            {
                category.LastAccessed = DateTime.UtcNow;
                result.Add(category);
            }
        }
        
        // If no cached categories, load from Redis
        if (result.Count == 0)
        {
            await LoadChannelCategoriesAsync(chatId);
            
            foreach (var category in _channelCategories.Values)
            {
                if (category.ChatId == chatId)
                {
                    category.LastAccessed = DateTime.UtcNow;
                    result.Add(category);
                }
            }
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
            
            var categories = await _repository.GetAllChannelCategoriesAsync(chatId);
            
            foreach (var kvp in categories)
            {
                var category = new BanphraseCategory
                {
                    CategoryName = kvp.Key,
                    Section = chatId.ToString(),
                    ChatId = chatId,
                    RegexPattern = kvp.Value,
                    LastAccessed = DateTime.UtcNow
                };
                category.CompileRegex();
                
                var key = $"{chatId}:{kvp.Key}";
                _channelCategories[key] = category;
            }
            
            _logger.LogDebug(
                "Loaded {Count} channel banphrase categories for {ChatId}",
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
        Guid chatId,
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
                await _repository.SetChannelCategoryAsync(chatId, categoryName, regexPattern);
                
                // Update cache
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
                
                _logger.LogInformation(
                    "Set channel banphrase category: {Category} for {ChatId}",
                    categoryName,
                    chatId);
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
        Guid chatId,
        string categoryName)
    {
        if (section.ToLowerInvariant() == "global")
        {
            return await _repository.GetGlobalCategoryAsync(categoryName);
        }
        else
        {
            return await _repository.GetChannelCategoryAsync(chatId, categoryName);
        }
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(
        string section,
        Guid chatId)
    {
        if (section.ToLowerInvariant() == "global")
        {
            return await _repository.GetGlobalCategoryNamesAsync();
        }
        else
        {
            return await _repository.GetChannelCategoryNamesAsync(chatId);
        }
    }

    public async Task<bool> DeleteCategoryAsync(
        string section,
        Guid chatId,
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
            await _repository.DeleteChannelCategoryAsync(chatId, categoryName);
            var key = $"{chatId}:{categoryName}";
            _channelCategories.TryRemove(key, out _);
            _logger.LogInformation(
                "Deleted channel banphrase category: {Category} for {ChatId}",
                categoryName,
                chatId);
        }
        
        return true;
    }
}