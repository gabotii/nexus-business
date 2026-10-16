using Nexus.Domain.Common;

namespace Nexus.Domain.Files;

public enum FilePurpose
{
    TenantLogo,
}

/// <summary>
/// Metadata of an uploaded file (R12). The bytes live in file storage under <see cref="StorageKey"/>, a name the
/// server generates; the client's file name is never used.
/// </summary>
public sealed class StoredFile : ITenantOwned
{
    private StoredFile()
    {
        ContentType = StorageKey = string.Empty;
        Sha256 = [];
    }

    public StoredFile(Guid tenantId, FilePurpose purpose, string contentType, long sizeBytes, byte[] sha256, Guid createdBy,
        DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Purpose = purpose;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        StorageKey = $"{tenantId:N}/{Id:N}";
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public FilePurpose Purpose { get; private set; }

    public string ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public byte[] Sha256 { get; private set; }

    public string StorageKey { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Image types accepted for logos, recognised by their first bytes ("magic numbers"), never by name or header.</summary>
public static class ImageFormats
{
    public const long MaxLogoBytes = 512 * 1024;

    /// <returns>The content type, or null for anything else (SVG included: it can carry scripts).</returns>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
