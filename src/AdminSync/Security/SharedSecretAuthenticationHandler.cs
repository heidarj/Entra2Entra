using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using AdminSync.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AdminSync.Security;

public class SharedSecretAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SharedSecret";
    private readonly ScimOptions _options;

    public SharedSecretAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        IOptions<ScimOptions> scimOptions)
        : base(schemeOptions, logger, encoder, clock)
    {
        _options = scimOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header"));
        }

        var header = headerValues.ToString();
        if (!header.StartsWith("SharedSecret ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization scheme"));
        }

        var provided = header.Substring("SharedSecret ".Length).Trim();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided),
                Encoding.UTF8.GetBytes(_options.SharedSecret)))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid shared secret"));
        }

        var identity = new ClaimsIdentity(Scheme.Name);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "scim-client"));
        identity.AddClaim(new Claim(ClaimTypes.Name, "SCIM Client"));
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
