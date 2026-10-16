namespace Nexus.Application.Common;

/// <summary>
/// The resource does not exist <em>or the caller may not know that it exists</em>. Resources of another tenant are
/// always reported as not found, never as forbidden, so identifiers cannot be probed.
/// </summary>
public sealed class NotFoundException(string message = "El recurso solicitado no existe.", string code = "not_found")
    : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The request conflicts with the current state (duplicate key, stale version, etc.). <see cref="Details"/> are extra,
/// non-sensitive fields for the client (e.g. the id of the existing duplicate) added to the problem response.
/// </summary>
public sealed class ConflictException(string code, string message, IReadOnlyDictionary<string, object?>? details = null)
    : Exception(message)
{
    public string Code { get; } = code;

    public IReadOnlyDictionary<string, object?> Details { get; } = details ?? new Dictionary<string, object?>();
}

/// <summary>The caller is a member of the tenant but lacks the permission for this operation.</summary>
public sealed class ForbiddenException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The credentials or the session are not valid. Always answered with the same generic message.</summary>
public sealed class UnauthenticatedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
