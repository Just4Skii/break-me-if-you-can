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

    /// <summary>
    /// Header used by cross-origin clients. GitHub Pages cannot rely on third-party
    /// cookies, so the same protected visitor id travels in this header instead.
    /// </summary>
    public const string HeaderName = "X-Sandbox-Token";

    private readonly IDataProtector _protector;

    public VisitorCookie(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("break-me-if-you-can.visitor.v1");
    }

    public bool TryResolve(HttpContext context, out Guid visitor)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Headers.TryGetValue(HeaderName, out var header) &&
            !string.IsNullOrWhiteSpace(header) &&
            TryUnprotect(header.ToString(), out visitor))
        {
            return true;
        }

        if (context.Request.Cookies.TryGetValue(CookieName, out var raw) &&
            !string.IsNullOrWhiteSpace(raw) &&
            TryUnprotect(raw, out visitor))
        {
            return true;
        }

        visitor = Guid.Empty;
        return false;
    }

    /// <summary>Protects a visitor id for use as a cross-origin header value.</summary>
    public string Protect(Guid visitor) => _protector.Protect(visitor.ToString());

    public Guid Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (TryResolve(context, out var existing))
        {
            return existing;
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

    private bool TryUnprotect(string value, out Guid visitor)
    {
        try
        {
            var unprotected = _protector.Unprotect(value);
            if (Guid.TryParse(unprotected, out visitor))
            {
                return true;
            }
        }
        catch (CryptographicException)
        {
            // Tampered or stale value: the caller will issue a fresh identity.
        }

        visitor = Guid.Empty;
        return false;
    }
}
