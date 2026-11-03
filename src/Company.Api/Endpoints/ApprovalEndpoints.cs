using System.Security.Claims;
using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Services;

using static Company.Api.Endpoints.EndpointResults;

namespace Company.Api.Endpoints;

/// <summary>The approvers' work on other people's months: what waits for them, looking at it, deciding on it</summary>
public static class ApprovalEndpoints
{
    public static void MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var approvals = app.MapGroup("/api/approvals").RequireAuthorization(Identities.RegisteredPolicy);

        approvals.MapGet("", (ClaimsPrincipal user, WorkflowService service) =>
            Run(() => service.PendingAsync(Identities.EmployeeCode(user)!)));

        approvals.MapGet("/{code}/{year:int}/{month:int}", (string code, int year, int month, ClaimsPrincipal user, WorkflowService service) =>
            Run(() => service.ReviewAsync(Identities.EmployeeCode(user)!, code, year, month)));

        approvals.MapGet("/{code}/{year:int}/{month:int}/revisions/{revision:int}", (string code, int year, int month, int revision, ClaimsPrincipal user, WorkflowService service) =>
            Run(() => service.RevisionAsync(Identities.EmployeeCode(user)!, code, year, month, revision)));

        approvals.MapGet("/{code}/{year:int}/{month:int}/print/{format}", (string code, int year, int month, string format, ClaimsPrincipal user, PrintService service) =>
            File(() => service.ReviewAsync(Identities.EmployeeCode(user)!, code, year, month, format))).RequireSheetExport();

        approvals.MapPost("/{code}/{year:int}/{month:int}/approve", (string code, int year, int month, ClaimsPrincipal user, WorkflowService service) =>
            Run(() => service.ApproveAsync(Identities.EmployeeCode(user)!, code, year, month)));

        approvals.MapPost("/{code}/{year:int}/{month:int}/return", (string code, int year, int month, ReturnRequest request, ClaimsPrincipal user, WorkflowService service) =>
            Run(() => service.ReturnAsync(Identities.EmployeeCode(user)!, code, year, month, request.Message)));
    }
}
