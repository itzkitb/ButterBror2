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
                        "version={Version} commit={Commit} branch={Branch}",
                        Version, BuildCommit, BuildBranch);
                }
                else
                {
                    _logger.LogWarning("Version file is malformed");
                }
            }
            catch
            {
                _logger.LogError("Failed to read version file");
            }
        }
        else
        {
            _logger.LogWarning("The version file was not found. If you built the core manually, we recommend creating a \"version\" file in the root directory of the program and filling it with the following template: https://github.com/itzkitb/ButterBror2/blob/main/version");
        }
    }
}