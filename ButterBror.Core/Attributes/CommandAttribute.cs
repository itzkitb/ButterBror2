using ButterBror.Core.Enums;

namespace ButterBror.Core.Attributes;

/// <summary>
/// Attribute to define command metadata for automatic registration
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class CommandAttribute : Attribute
{
    public string Name { get; set; }
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public int CooldownSeconds { get; set; } = 0;
    public string[] RequiredPermissions { get; set; } = Array.Empty<string>();
    public string ArgumentsHelpText { get; set; } = "";
    public string Id { get; set; } = "";
    public PlatformCompatibilityType PlatformCompatibilityType { get; set; } = PlatformCompatibilityType.Whitelist;
    public string[] PlatformCompatibilityList { get; set; } = Array.Empty<string>();
}