using ButterBror.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class BotCoreInfo : IBotCoreInfo
{
    public Version Version { private set; get; } = new Version(1, 0, 0);
    public string BuildCommit { private set; get; } = "unknown";
    public string BuildBranch { private set; get; } = "unknown";
    public string CommitTitle { private set; get; } = "unknown";
    public string RepositoryUrl { private set; get; } = "unknown";
    private ILogger _logger;

    public BotCoreInfo(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetService<ILogger<BotCoreInfo>>()!;
    }
    
    public void Initialize()
    {
        string versionFilePath = Path.Combine(AppContext.BaseDirectory, "version");
        if (File.Exists(versionFilePath))
        {
            try
            {
                string[] lines = File.ReadAllLines(versionFilePath);
                if (lines.Length >= 7)
                {
                    Version = Version.Parse(lines[2].Trim());
                    BuildCommit = lines[3].Trim();
                    BuildBranch = lines[4].Trim();
                    CommitTitle = lines[5].Trim();
                    RepositoryUrl = lines[6].Trim();

                    _logger.LogInformation(
                        "ButterBror. version={Version}, commit={Commit}, branch={Branch}, repo='{RepositoryUrl}'",
                        Version, BuildCommit, BuildBranch, RepositoryUrl);
                }
                else
                {
                    _logger.LogWarning("Version file is malformed. lines={Lines}", lines.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to read version file. message='{Message}'", ex.Message);
            }
        }
        else
        {
            _logger.LogWarning("The version file was not found. If you built the core manually, we recommend creating a \"version\" file in the root of the program and filling it with the template: https://github.com/itzkitb/ButterBror2/blob/main/version. path='{VersionFilePath}'", versionFilePath);
        }
    }
}