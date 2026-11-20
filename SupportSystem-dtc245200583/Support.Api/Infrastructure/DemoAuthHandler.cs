using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Support.Api.Infrastructure;

public sealed class DemoAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers["X-Demo-User"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userId)) userId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        if (!Guid.TryParse(userId, out _)) return Task.FromResult(AuthenticateResult.Fail("X-Demo-User must be a UUID."));
        var role = Request.Headers["X-Demo-Role"].FirstOrDefault() ?? "Customer";
        if (role is not ("Customer" or "Agent" or "Admin")) return Task.FromResult(AuthenticateResult.Fail("Unsupported demo role."));
        var claims = new[] { new Claim("sub", userId), new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
