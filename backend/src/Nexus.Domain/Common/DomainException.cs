namespace Nexus.Domain.Common;

/// <summary>
/// A business rule was violated. <see cref="Code"/> is a stable machine-readable identifier (snake_case) that clients
/// may branch on; the message is safe to show to end users.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
