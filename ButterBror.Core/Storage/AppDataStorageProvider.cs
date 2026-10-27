using ButterBror.Core.Interfaces;

namespace ButterBror.Core.Storage;

public class AppDataStorageProvider : IAppDataPathProvider
{
    public string GetAppDataPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "tupid",
                "butterbror"
            );
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share",
                "tupid",
                "butterbror"
            );
        }

        throw new PlatformNotSupportedException("Unsupported operating system");
    }
}