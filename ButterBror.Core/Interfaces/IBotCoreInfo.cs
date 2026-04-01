namespace ButterBror.Core.Interfaces;

public interface IBotCoreInfo
{
    /// <summary>
    /// Gets the version of the bot core
    /// </summary>
    Version Version { get; }

    /// <summary>
    /// Gets the build commit of the bot core
    /// </summary>
    string BuildCommit { get; }

    /// <summary>
    /// Gets the branch name of the bot core
    /// </summary>
    string BuildBranch { get; }

    /// <summary>
    /// Gets the commit title of the bot core
    /// </summary>
    string CommitTitle { get; }

    /// <summary>
    /// Gets the repository URL of the bot core
    /// </summary>
    string RepositoryUrl { get; }

    /// <summary>
    /// Reading from the version file
    /// </summary>
    void Initialize();
}