using ButterBror.Core.Contracts;
using ButterBror.Core.Models.Commands;
using MediatR;

namespace ButterBror.Core.Abstractions;

/// <summary>
/// Interface for commands that have metadata but also work with MediatR
/// </summary>
public interface IMetadataCommand : ICommand
{
    /// <summary>
    /// Gets the metadata for this command
    /// </summary>
    /// <returns>Command metadata</returns>
    ICommandMetadata GetMetadata();
}