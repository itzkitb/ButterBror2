using ButterBror.Core.Attributes;
using ButterBror.Core.Contracts;
using ButterBror.Core.Enums;
using ButterBror.Core.Models.Commands;
using MediatR;

namespace ButterBror.Core.Wrappers;

/// <summary>
/// Wrapper that implements ICommandMetadata from CommandAttribute
/// </summary>
public class CommandMetadataWrapper : ICommandMetadata
{
    private readonly CommandAttribute _attribute;

    public CommandMetadataWrapper(CommandAttribute attribute)
    {
        _attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
    }

    public string Name => _attribute.Name;
    public List<string> Aliases => _attribute.Aliases.ToList();
    public int CooldownSeconds => _attribute.CooldownSeconds;
    public List<string> RequiredPermissions => _attribute.RequiredPermissions.ToList();
    public string ArgumentsHelpText => _attribute.ArgumentsHelpText;
    public string Id => _attribute.Id;
    public PlatformCompatibilityType PlatformCompatibilityType => _attribute.PlatformCompatibilityType;
    public List<string> PlatformCompatibilityList => _attribute.PlatformCompatibilityList.ToList();
}