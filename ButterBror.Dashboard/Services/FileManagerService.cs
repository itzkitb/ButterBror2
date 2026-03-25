using ButterBror.Core.Interfaces;

namespace ButterBror.Dashboard.Services;

public record FileSystemEntry(
    string Name,
    string RelativePath,
    bool IsDirectory,
    long SizeBytes,
    DateTime ModifiedAt
);

public class FileManagerService
{
    private readonly string _root;
    private const long MaxUploadBytes = 100 * 1024 * 1024; // 100 MB

    public FileManagerService(IAppDataPathProvider pathProvider)
    {
        _root = pathProvider.GetAppDataPath();
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Нормализует путь и проверяет, что он находится внутри корневой папки.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Выбрасывается при попытке выхода за пределы корня</exception>
    private string ResolveSafe(string relativePath)
    {
        // Очищаем путь от потенциально опасных символов
        var sanitized = relativePath?.Replace('\\', '/') ?? string.Empty;
        
        // Удаляем начальные слеши
        sanitized = sanitized.TrimStart('/');
        
        // Комбинируем с корнем
        var combined = Path.Combine(_root, sanitized);
        
        // Получаем полный канонический путь
        var fullPath = Path.GetFullPath(combined);
        
        // Проверяем, что путь начинается с корня (защита от path traversal)
        var rootNormalized = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar);
        
        if (!fullPath.StartsWith(rootNormalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Access denied: path traversal detected");
        }
        
        // Проверяем символические ссылки - не позволяем выходить за пределы корня
        if (Directory.Exists(fullPath) || File.Exists(fullPath))
        {
            var realPath = Path.GetFullPath(fullPath);
            if (!realPath.StartsWith(rootNormalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Access denied: symlink target outside root");
            }
        }
        
        return fullPath;
    }

    public async Task<FileSystemEntry[]> ListDirectoryAsync(string relativePath)
    {
        var fullPath = ResolveSafe(relativePath);
        
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory not found: {relativePath}");
        }

        var entries = new List<FileSystemEntry>();
        var rootDir = new DirectoryInfo(fullPath);

        // Добавляем папки
        foreach (var dir in rootDir.GetDirectories())
        {
            entries.Add(new FileSystemEntry(
                dir.Name,
                Path.Combine(relativePath, dir.Name).Replace('\\', '/'),
                IsDirectory: true,
                SizeBytes: 0,
                ModifiedAt: dir.LastWriteTimeUtc
            ));
        }

        // Добавляем файлы
        foreach (var file in rootDir.GetFiles())
        {
            entries.Add(new FileSystemEntry(
                file.Name,
                Path.Combine(relativePath, file.Name).Replace('\\', '/'),
                IsDirectory: false,
                SizeBytes: file.Length,
                ModifiedAt: file.LastWriteTimeUtc
            ));
        }

        return entries.ToArray();
    }

    public async Task UploadFileAsync(string relativeDir, string fileName, Stream content)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be empty", nameof(fileName));
        }

        // Проверяем имя файла на опасные символы
        var invalidChars = Path.GetInvalidFileNameChars();
        if (fileName.IndexOfAny(invalidChars) >= 0)
        {
            throw new ArgumentException("File name contains invalid characters", nameof(fileName));
        }

        var fullPath = ResolveSafe(relativeDir);
        
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        var filePath = Path.Combine(fullPath, fileName);
        
        // Проверяем после комбинирования
        var resolvedPath = ResolveSafe(Path.Combine(relativeDir, fileName).Replace('\\', '/'));

        // Ограничение размера
        if (content.CanSeek)
        {
            if (content.Length > MaxUploadBytes)
            {
                throw new InvalidOperationException($"File size exceeds maximum allowed size of {MaxUploadBytes / (1024 * 1024)} MB");
            }
        }
        else
        {
            // Если поток не поддерживает Seek, читаем в буфер с проверкой размера
            using var ms = new MemoryStream();
            var buffer = new byte[81920]; // 80 KB
            int bytesRead;
            long totalBytes = 0;
            
            while ((bytesRead = await content.ReadAsync(buffer, default)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > MaxUploadBytes)
                {
                    throw new InvalidOperationException($"File size exceeds maximum allowed size of {MaxUploadBytes / (1024 * 1024)} MB");
                }
                await ms.WriteAsync(buffer.AsMemory(0, bytesRead), default);
            }
            
            await File.WriteAllBytesAsync(resolvedPath, ms.ToArray(), default);
            return;
        }

        using var fileStream = File.Create(resolvedPath);
        await content.CopyToAsync(fileStream);
    }

    public async Task DeleteAsync(string relativePath)
    {
        var fullPath = ResolveSafe(relativePath);
        
        // Запрещаем удалять корень
        if (string.Equals(fullPath, _root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Cannot delete root directory");
        }

        if (Directory.Exists(fullPath))
        {
            var dir = new DirectoryInfo(fullPath);
            if (dir.EnumerateFileSystemInfos().Any())
            {
                throw new InvalidOperationException("Cannot delete non-empty directory");
            }
            Directory.Delete(fullPath);
        }
        else if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        else
        {
            throw new FileNotFoundException($"File or directory not found: {relativePath}");
        }
    }

    public async Task CreateDirectoryAsync(string relativePath)
    {
        var fullPath = ResolveSafe(relativePath);
        
        // Запрещаем создавать папку в корне с пустым именем
        if (string.Equals(fullPath, _root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Cannot create directory at root");
        }

        Directory.CreateDirectory(fullPath);
    }

    public async Task RenameAsync(string relativePath, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("New name cannot be empty", nameof(newName));
        }

        // Проверяем имя на опасные символы
        var invalidChars = Path.GetInvalidFileNameChars();
        if (newName.IndexOfAny(invalidChars) >= 0)
        {
            throw new ArgumentException("New name contains invalid characters", nameof(newName));
        }

        // newName должно быть только именем, не путем
        if (newName.Contains('/') || newName.Contains('\\'))
        {
            throw new ArgumentException("New name must be a file/folder name, not a path", nameof(newName));
        }

        var fullPath = ResolveSafe(relativePath);
        
        // Запрещаем переименовывать корень
        if (string.Equals(fullPath, _root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Cannot rename root directory");
        }

        if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File or directory not found: {relativePath}");
        }

        var parentDir = Path.GetDirectoryName(fullPath)!;
        var newPath = Path.Combine(parentDir, newName);
        
        // Проверяем новый путь на безопасность
        var resolvedNewPath = Path.GetFullPath(newPath);
        var rootNormalized = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar);
        
        if (!resolvedNewPath.StartsWith(rootNormalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Access denied: rename target outside root");
        }

        if (File.Exists(fullPath))
        {
            File.Move(fullPath, newPath);
        }
        else
        {
            Directory.Move(fullPath, newPath);
        }
    }

    public async Task<Stream> GetFileStreamAsync(string relativePath)
    {
        var fullPath = ResolveSafe(relativePath);
        
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {relativePath}");
        }

        if (Directory.Exists(fullPath))
        {
            throw new UnauthorizedAccessException("Cannot download a directory");
        }

        return File.OpenRead(fullPath);
    }
}
