using ButterBror.Core.Interfaces;

namespace ButterBror.Infrastructure.Storage;

public class AppDataStorageProvider : IAppDataPathProvider
{
    public string GetAppDataPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SillyApps",
                "ButterBror2"
            );
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share",
                "SillyApps",
                "ButterBror2"
            );
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported operating system");
        }
    }
}