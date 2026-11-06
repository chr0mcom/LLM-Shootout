using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace LlmShootout.Web.Auth;

/// <summary>The one-password login: the session cookie, the gate in front of everything, and the checks that protect state-changing requests.</summary>
internal static class PasswordGate
{
    public const string LoginRateLimit = "login";
    private const string PasswordClaim = "pv";

    private static readonly HashSet<string> PublicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/login", "/login.html", "/login.js", "/site.css", "/favicon.svg", "/health",
    };

    public static void AddPasswordGate(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "__Host-shootout";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromHours(12);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = async context =>
            {
                var password = context.HttpContext.RequestServices.GetRequiredService<IOptions<WebOptions>>().Value.Password;
                if (context.Principal?.FindFirstValue(PasswordClaim) != Fingerprint(password))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            };
            options.Events.OnRedirectToLogin = context => Reject(context.Response, StatusCodes.Status401Unauthorized, "/login");
            options.Events.OnRedirectToAccessDenied = context => Reject(context.Response, StatusCodes.Status403Forbidden, "/login");
        });
        services.AddAuthorization();
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(LoginRateLimit, context =>
            {
                var attempts = context.RequestServices.GetRequiredService<IOptions<WebOptions>>().Value.LoginAttemptsPerMinute;
                return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = attempts, Window = TimeSpan.FromMinutes(1) });
            });
        });
        _ = configuration;
    }

    /// <summary>Lets only the login page, its stylesheet and the health check through without a session.</summary>
    public static IApplicationBuilder UseGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (PublicPaths.Contains(context.Request.Path.Value ?? "/") || context.User.Identity?.IsAuthenticated == true)
        {
            await next();
            return;
        }

        await Reject(context.Response, StatusCodes.Status401Unauthorized, "/login", context.Request.Path.StartsWithSegments("/api"));
    });

    /// <summary>Refuses state-changing requests that do not come from this site.</summary>
    public static IApplicationBuilder UseSameOriginOnly(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) || IsSameOrigin(context.Request))
        {
            await next();
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Cross-site request refused.");
    });

    public static bool PasswordMatches(string? given, string expected)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(given ?? string.Empty));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    /// <summary>A sign of the current password stored in the session, so that changing the password ends every open session.</summary>
    private static string Fingerprint(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"shootout-session:{password}")))[..16];

    public static async Task SignInAsync(HttpContext context, string password)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "operator"), new Claim(PasswordClaim, Fingerprint(password))], CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false });
    }

    private static bool IsSameOrigin(HttpRequest request)
    {
        if (request.Headers.Origin.Count == 1 && Uri.TryCreate(request.Headers.Origin[0], UriKind.Absolute, out var origin))
        {
            return string.Equals(origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
        }

        return request.Headers["Sec-Fetch-Site"].ToString() is "same-origin" or "none";
    }

    private static Task Reject(HttpResponse response, int status, string loginPath, bool api = true)
    {
        if (api)
        {
            response.StatusCode = status;
            return Task.CompletedTask;
        }

        response.Redirect(loginPath);
        return Task.CompletedTask;
    }
}
