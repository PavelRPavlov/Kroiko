using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ATAFurniture.Server.Auth;

/// <summary>
/// DEVELOPMENT-ONLY authentication handler. It auto-signs-in a fixed local user so the app
/// can be exercised without an Azure AD B2C tenant (e.g. when the B2C secrets are unavailable).
///
/// It is registered ONLY when the environment is Development AND no <c>AzureAd:ClientId</c> is
/// configured (see <see cref="Startup.ConfigureServices"/>), so it can never take effect in
/// production. The emitted claim types deliberately mirror the B2C claims that
/// <see cref="DataAccess.UserContextService"/> reads (oid / name / emails / extension_*), so the
/// downstream user/credit lookup behaves exactly as it does with a real sign-in.
///
/// Override the synthetic user via a "DevAuth" configuration section (user-secrets) if needed.
/// </summary>
public sealed class DevAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevBypass";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var devAuth = configuration.GetSection("DevAuth");

        // Claim types MUST match the constants in UserContextService.
        var claims = new[]
        {
            new Claim("oid", devAuth["Oid"] ?? "dev-00000000-0000-0000-0000-000000000001"),
            new Claim("name", devAuth["Name"] ?? "Local Dev User"),
            new Claim("emails", devAuth["Email"] ?? "dev@example.com"),
            new Claim("extension_MobileNumber", devAuth["MobileNumber"] ?? "0888000000"),
            new Claim("extension_CompanyName", devAuth["CompanyName"] ?? "Local Dev Company"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
