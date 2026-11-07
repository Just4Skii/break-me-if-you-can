using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace BreakMeIfYouCan.Api.Infrastructure;

/// <summary>
/// Anonymous sandbox identity. The cookie holds a random visitor id protected by the
/// data protection key ring, so it cannot be forged or enumerated. There is no
/// registration and no personal data.
/// </summary>
public sealed class VisitorCookie
{
    public const string CookieName = "bm_visitor";

    private readonly IDataProtector _protector;

    public VisitorCookie(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("break-me-if-you-can.visitor.v1");
    }

    public Guid Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Cookies.TryGetValue(CookieName, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                var value = _protector.Unprotect(raw);
                if (Guid.TryParse(value, out var existing))
                {
                    return existing;
                }
            }
            catch (CryptographicException)
            {
                // Tampered or stale cookie: issue a fresh identity below.
            }
        }

        var visitor = Guid.NewGuid();
        var protectedValue = _protector.Protect(visitor.ToString());
        context.Response.Cookies.Append(CookieName, protectedValue, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/",
            MaxAge = TimeSpan.FromDays(30),
            IsEssential = true,
        });

        return visitor;
    }
}
