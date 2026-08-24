using ButterBror.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ButterBror.Infrastructure.Services;

public class BotCoreInfo(ILogger<BotCoreInfo> logger) : IBotCoreInfo
{
    public Version Version { private set; get; } = new(0, 0, 0);
    public string BuildCommit { private set; get; } = "unknown";
    public string BuildBranch { private set; get; } = "unknown";
    public string CommitTitle { private set; get; } = "unknown";
    public string RepositoryUrl { private set; get; } = "unknown";

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

                    logger.LogInformation(
                        "hi. v={Version}, commit={Commit}, branch={Branch}",
                        Version, BuildCommit, BuildBranch);
                }
                else
                {
                    logger.LogWarning("version file is malformed. lines={Lines}", lines.Length);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "failed to read version file");
            }
        }
        else
        {
            logger.LogWarning("create a 'version' file in the root of the program and fill it with the template: https://github.com/itzkitb/ButterBror2/blob/main/version");
        }
    }
}