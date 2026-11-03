using System.Security.Claims;
using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Services;

using static Company.Api.Endpoints.EndpointResults;

namespace Company.Api.Endpoints;

/// <summary>Sign-in with a user name and password, and the administrator's management of those credentials</summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app, AuthMode mode)
    {
        // Signing in exists only where the password is how people come in; the administrator can prepare the user names in any mode
        if (mode == AuthMode.Password)
        {
            app.MapPost("/api/auth/login", (LoginRequest request, CredentialService service) => Run(() => service.LoginAsync(request))).AllowAnonymous();

            // Allowed also while the password must be changed
            var signedIn = app.MapGroup("/api/auth").RequireAuthorization();
            signedIn.MapPost("/logout", async (ClaimsPrincipal user, CredentialService service) =>
            {
                if (int.TryParse(user.FindFirstValue(PasswordAuthHandler.SessionClaim), out var id)) await service.LogoutAsync(id);
                return Results.NoContent();
            });

            signedIn.MapPost("/change-password", (ChangePasswordRequest request, ClaimsPrincipal user, CredentialService service) =>
                Run(async () =>
                {
                    await service.ChangePasswordAsync(Identities.EmployeeCode(user)!, request);
                    return new { changed = true };
                })).RequireAuthorization(Identities.RegisteredPolicy);

        }

        var admin = app.MapGroup("/api/admin").RequireAuthorization(Identities.AdminPolicy);
        admin.MapGet("/credentials", (CredentialService service) => Run(() => service.ListAsync()));
        admin.MapPut("/employees/{code}/credentials", (string code, SetCredentialsRequest request, CredentialService service) =>
            Run(async () =>
            {
                await service.SetAsync(code, request);
                return new { saved = true };
            }));
        admin.MapDelete("/employees/{code}/credentials", (string code, CredentialService service) =>
            Run(async () =>
            {
                await service.RemoveAsync(code);
                return new { removed = true };
            }));
    }
}
