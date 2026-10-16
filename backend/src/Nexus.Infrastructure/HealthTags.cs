namespace Nexus.Infrastructure;

public static class HealthTags
{
    /// <summary>Checks that must pass before the instance receives traffic (<c>/health/ready</c>).</summary>
    public const string Ready = "ready";
}
