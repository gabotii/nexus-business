using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nexus.Application.Abstractions;

namespace Nexus.Infrastructure.Files;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Folder for uploaded files. Relative paths are anchored to the API content root (development); Production must
    /// point at a persistent volume (Docker Compose: <c>/files</c>).
    /// </summary>
    public string RootPath { get; set; } = "storage";
}

/// <summary>
/// Files on a local folder or mounted volume. Keys come from the server (<c>{tenant}/{file}</c>, hex only); they are
/// still checked so that a bug can never read or write outside the root.
/// </summary>
internal sealed class LocalFileStorage(IOptions<StorageOptions> options, IHostEnvironment environment) : IFileStorage
{
    private string Root => Path.GetFullPath(options.Value.RootPath, environment.ContentRootPath);

    public async Task SaveAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write to a temporary name first so a crash never leaves a half-written file under the real key.
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, content.ToArray(), ct);
        File.Move(temporary, path, overwrite: true);
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathOf(string key)
    {
        if (key.Length == 0 || !key.All(c => char.IsAsciiHexDigit(c) || c == '/'))
        {
            throw new ArgumentException("Invalid storage key.", nameof(key));
        }

        var root = Root;
        var path = Path.GetFullPath(Path.Combine(root, key));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage key escapes the storage root.", nameof(key));
        }

        return path;
    }
}
