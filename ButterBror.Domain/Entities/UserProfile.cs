using System;
using System.Collections.Generic;
using System.Text;

namespace ButterBror.Domain.Entities;

public class UserProfile
{
    public Guid UnifiedUserId { get; set; }
    public Dictionary<string, string> PlatformIds { get; set; } = new(); // platform -> platformSpecificId
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastActive { get; set; }
    public Dictionary<string, Int64> Statistics { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public string PreferredLocale { get; set; } = "EN_US";

    public void AddPlatformId(string platform, string platformId)
    {
        PlatformIds[platform.ToLowerInvariant()] = platformId;
    }

    public bool HasPlatformId(string platform, string platformId)
    {
        return PlatformIds.TryGetValue(platform.ToLowerInvariant(), out var id) && id == platformId;
    }
}