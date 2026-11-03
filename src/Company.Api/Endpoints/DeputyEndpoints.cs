using System.Security.Claims;
using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Services;

using static Company.Api.Endpoints.EndpointResults;

namespace Company.Api.Endpoints;

/// <summary>Who stands in for an approver while they are away: the approver chooses, the administrator may as well</summary>
public static class DeputyEndpoints
{
    public static void MapDeputyEndpoints(this IEndpointRouteBuilder app)
    {
        var deputies = app.MapGroup("/api/deputies").RequireAuthorization(Identities.RegisteredPolicy);

        deputies.MapGet("", (ClaimsPrincipal user, DeputyService service) => Run(() => service.ListAsync(Identities.EmployeeCode(user)!)));

        deputies.MapGet("/candidates", (ClaimsPrincipal user, DeputyService service) => Run(() => service.CandidatesAsync(Identities.EmployeeCode(user)!)));

        deputies.MapPost("", (AddDeputyRequest request, ClaimsPrincipal user, DeputyService service) =>
        {
            var code = Identities.EmployeeCode(user)!;
            return Run(() => service.AddAsync(code, code, request));
        });

        deputies.MapDelete("/{id:int}", (int id, ClaimsPrincipal user, DeputyService service) =>
            Run(async () =>
            {
                await service.RemoveAsync(Identities.EmployeeCode(user)!, user.IsInRole(Roles.Admin), id);
                return new { removed = true };
            }));

        var admin = app.MapGroup("/api/admin").RequireAuthorization(Identities.AdminPolicy);
        admin.MapGet("/deputies", (DeputyService service) => Run(() => service.ListAllAsync()));
        admin.MapPost("/employees/{code}/deputies", (string code, AddDeputyRequest request, ClaimsPrincipal user, DeputyService service) =>
            Run(() => service.AddAsync(Identities.EmployeeCode(user)!, code, request)));
    }
}
