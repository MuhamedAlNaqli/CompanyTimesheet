using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Extensions.Options;

namespace Company.Api.Auth;

/// <summary>How callers prove who they are</summary>
public enum AuthMode
{
    /// <summary>User name and password created by the administrator; the server hands out a session token (the default)</summary>
    Password,

    /// <summary>On-premises Active Directory: Kerberos or NTLM through the Windows account of the caller (primary)</summary>
    Windows,

    /// <summary>Microsoft Entra ID: a bearer token</summary>
    Entra,

    /// <summary>Both: a request with a bearer token is validated as Entra, any other through Windows</summary>
    Both,

    /// <summary>Development and tests only: the account comes from the X-Test-User header</summary>
    Test
}

public static class AuthenticationSetup
{
    public const string SmartScheme = "Smart";
    public const string TestScheme = "Test";
    public const string TestHeader = "X-Test-User";

    public static AuthMode ReadMode(IConfiguration configuration) =>
        Enum.TryParse<AuthMode>(configuration["Authentication:Mode"], ignoreCase: true, out var mode) ? mode : AuthMode.Password;

    /// <summary>
    /// Registers the schemes of the chosen mode. A mode that cannot work stops the server at start-up with a clear
    /// message instead of failing later at the first sign-in.
    /// </summary>
    public static AuthMode AddCompanyAuthentication(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var mode = ReadMode(configuration);
        var auth = builder.Services.AddAuthentication();

        // Windows and Entra sign-in are built but switched off until the company chooses them
        if (mode is AuthMode.Windows or AuthMode.Entra or AuthMode.Both && !configuration.GetValue<bool>("Features:WindowsLogin"))
            throw new InvalidOperationException($"The sign-in mode {mode} is switched off. Set Features:WindowsLogin to true to use it, or use Authentication:Mode=Password.");

        void AddEntra()
        {
            var tenant = configuration["Authentication:Entra:TenantId"];
            var clientId = configuration["Authentication:Entra:ClientId"];
            if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("Authentication:Entra:TenantId and Authentication:Entra:ClientId are required when the mode is Entra or Both.");

            auth.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{tenant}/v2.0";
                options.Audience = configuration["Authentication:Entra:Audience"] ?? clientId;
                options.TokenValidationParameters.ValidateIssuer = true;
            });
        }

        switch (mode)
        {
            case AuthMode.Password:
                builder.Services.Configure<AuthenticationOptions>(o => o.DefaultScheme = o.DefaultChallengeScheme = PasswordAuthHandler.Scheme);
                auth.AddScheme<AuthenticationSchemeOptions, PasswordAuthHandler>(PasswordAuthHandler.Scheme, _ => { });
                break;

            case AuthMode.Windows:
                builder.Services.Configure<AuthenticationOptions>(o => o.DefaultScheme = o.DefaultChallengeScheme = NegotiateDefaults.AuthenticationScheme);
                auth.AddNegotiate();
                break;

            case AuthMode.Entra:
                builder.Services.Configure<AuthenticationOptions>(o => o.DefaultScheme = o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme);
                AddEntra();
                break;

            case AuthMode.Both:
                AddEntra();
                auth.AddNegotiate();
                auth.AddPolicyScheme(SmartScheme, SmartScheme, options =>
                {
                    options.ForwardDefaultSelector = context => SelectScheme(context.Request.Headers.Authorization.ToString());
                });
                builder.Services.Configure<AuthenticationOptions>(o => o.DefaultScheme = o.DefaultChallengeScheme = SmartScheme);
                break;

            case AuthMode.Test:
                if (!builder.Environment.IsDevelopment())
                    throw new InvalidOperationException("The Test authentication mode is only allowed in the Development environment.");

                // Anybody who can reach a Test server can be anybody: it may listen on this computer only
                var urls = configuration["urls"] ?? configuration["ASPNETCORE_URLS"] ?? "";
                foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!Uri.TryCreate(url.Replace("+", "localhost").Replace("*", "0.0.0.0"), UriKind.Absolute, out var address) || !(address.IsLoopback || address.Host == "localhost"))
                        throw new InvalidOperationException($"The Test authentication mode may only listen on this computer (localhost), not on {url}.");
                }

                builder.Services.Configure<AuthenticationOptions>(o => o.DefaultScheme = o.DefaultChallengeScheme = TestScheme);
                auth.AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, _ => { });
                break;
        }

        return mode;
    }

    /// <summary>Bearer tokens go to Entra; everything else (the Negotiate header or none) goes through Windows</summary>
    public static string SelectScheme(string authorizationHeader) =>
        authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? JwtBearerDefaults.AuthenticationScheme
            : NegotiateDefaults.AuthenticationScheme;
}

/// <summary>Sign-in for development and tests: whoever the X-Test-User header says. Never registered outside Development.</summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AuthenticationSetup.TestHeader, out var account) || string.IsNullOrWhiteSpace(account))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, account.ToString()) }, TestSchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestSchemeName)));
    }

    private const string TestSchemeName = AuthenticationSetup.TestScheme;
}
