using System;
using System.Collections.Generic;
using System.Text;

namespace ButterBror.Domain.Entities;

public class UserProfile
{
    public Guid UnifiedUserId { get; set; }
    public Dictionary<string, string> PlatformIds { get; } = new(); // platform -> platformSpecificId
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastActive { get; set; }
    public Dictionary<string, object> Statistics { get; } = new();
    public List<string> Permissions { get; } = new();

    public void AddPlatformId(string platform, string platformId)
    {
        PlatformIds[platform.ToLowerInvariant()] = platformId;
    }

    public bool HasPlatformId(string platform, string platformId)
    {
        return PlatformIds.TryGetValue(platform.ToLowerInvariant(), out var id) && id == platformId;
    }
}