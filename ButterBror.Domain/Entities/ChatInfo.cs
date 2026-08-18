namespace ButterBror.Domain.Entities;

public class ChatInfo
{
    public Guid UnifiedId { get; set; }
    public string PlatformId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public object? ExtraData { get; set; } = null;
}