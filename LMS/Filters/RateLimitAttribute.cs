using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections.Concurrent;

namespace LeadManagementSystem.Filters;

/// <summary>
/// Rate limiting filter to prevent brute force attacks on sensitive endpoints.
/// Tracks failed attempts by IP address and email, locks account temporarily after max attempts.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class RateLimitAttribute : ActionFilterAttribute
{
    private static readonly ConcurrentDictionary<string, (int Attempts, DateTime LockUntil)> _attemptTracker = new();
    private const int MaxAttempts = 5;
    private const int LockoutMinutes = 15;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var ipAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"login_{ipAddress}";

        // Check if IP is currently locked
        if (_attemptTracker.TryGetValue(key, out var record))
        {
            if (DateTime.UtcNow < record.LockUntil)
            {
                var remainingSeconds = (int)(record.LockUntil - DateTime.UtcNow).TotalSeconds;
                context.Result = new StatusCodeResult(429); // Too Many Requests
                context.HttpContext.Response.Headers["Retry-After"] = remainingSeconds.ToString();
                return;
            }
            else
            {
                // Lock expired, reset
                _attemptTracker.TryRemove(key, out _);
            }
        }

        base.OnActionExecuting(context);
    }

    /// <summary>
    /// Call this after a failed login attempt to increment counter
    /// </summary>
    public static void RecordFailedAttempt(HttpContext context)
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"login_{ipAddress}";

        var (attempts, lockUntil) = _attemptTracker.GetOrAdd(key, _ => (0, DateTime.UtcNow));

        if (DateTime.UtcNow >= lockUntil)
        {
            // Reset if lock has expired
            attempts = 0;
        }

        attempts++;

        if (attempts >= MaxAttempts)
        {
            lockUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
        }

        _attemptTracker[key] = (attempts, lockUntil);
    }

    /// <summary>
    /// Call this after a successful login to clear failed attempts
    /// </summary>
    public static void ClearAttempts(HttpContext context)
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"login_{ipAddress}";
        _attemptTracker.TryRemove(key, out _);
    }
}
