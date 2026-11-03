using System.Security.Claims;
using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Services;

using static Company.Api.Endpoints.EndpointResults;

namespace Company.Api.Endpoints;

/// <summary>The people who report to the caller: who has submitted the month, and a reminder to one of them</summary>
public static class TeamEndpoints
{
    public static void MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        var team = app.MapGroup("/api/team").RequireAuthorization(Identities.RegisteredPolicy);

        team.MapGet("/{year:int}/{month:int}/status", (int year, int month, ClaimsPrincipal user, TeamService service) =>
            Run(() => service.StatusAsync(Identities.EmployeeCode(user)!, year, month)));

        team.MapPost("/{year:int}/{month:int}/remind/{code}", (int year, int month, string code, ClaimsPrincipal user, TeamService service) =>
            Run(async () => new ReminderSentDto(await service.RemindAsync(Identities.EmployeeCode(user)!, code, year, month))));
    }
}
