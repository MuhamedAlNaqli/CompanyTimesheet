using System.Security.Claims;
using Company.Api.Auth;
using Company.Api.Contracts;
using Company.Api.Services;

using static Company.Api.Endpoints.EndpointResults;

namespace Company.Api.Endpoints;

/// <summary>The caller's own month: reading it, filling it in, submitting it, answering the approvers, looking at what was submitted, printing it</summary>
public static class TimesheetEndpoints
{
    public static void MapTimesheetEndpoints(this IEndpointRouteBuilder app)
    {
        var sheet = app.MapGroup("/api/timesheet/{year:int}/{month:int}").RequireAuthorization(Identities.RegisteredPolicy);

        // Reading and filling in
        sheet.MapGet("", (int year, int month, ClaimsPrincipal user, TimesheetService service) =>
            Run(() => service.GetAsync(Identities.EmployeeCode(user)!, year, month)));

        sheet.MapPut("/days/{date}", (int year, int month, DateOnly date, SetEntryRequest request, ClaimsPrincipal user, TimesheetService service) =>
            Run(() => service.SetEntryAsync(Identities.EmployeeCode(user)!, year, month, date, request)));

        sheet.MapDelete("/days/{date}/{code:int}", (int year, int month, DateOnly date, int code, ClaimsPrincipal user, TimesheetService service) =>
            Run(() => service.ClearEntryAsync(Identities.EmployeeCode(user)!, year, month, date, code)));

        sheet.MapPost("/fill-standard-days", (int year, int month, ClaimsPrincipal user, TimesheetService service) =>
            Run(() => service.FillStandardDaysAsync(Identities.EmployeeCode(user)!, year, month)));

        sheet.MapPost("/absences/preview", (int year, int month, AbsenceRangeRequest request, ClaimsPrincipal user, TimesheetService service) =>
            Run(() => service.PreviewAbsenceAsync(Identities.EmployeeCode(user)!, year, month, request)));

        sheet.MapPost("/absences", (int year, int month, AbsenceRangeRequest request, ClaimsPrincipal user, TimesheetService service) =>
            Run(() => service.ApplyAbsenceAsync(Identities.EmployeeCode(user)!, year, month, request)));

        sheet.MapPost("/overtime", (int year, int month, OvertimeRequest request, ClaimsPrincipal user, TimesheetService service) =>
            Run(() => service.AddOvertimeAsync(Identities.EmployeeCode(user)!, year, month, request)));

        sheet.MapPut("/overtime/{id:int}", (int year, int month, int id, OvertimeRequest request, ClaimsPrincipal user, TimesheetService service) =>
            Run(() => service.UpdateOvertimeAsync(Identities.EmployeeCode(user)!, year, month, id, request)));

        sheet.MapDelete("/overtime/{id:int}", (int year, int month, int id, ClaimsPrincipal user, TimesheetService service) =>
            Run(() => service.DeleteOvertimeAsync(Identities.EmployeeCode(user)!, year, month, id)));

        // Submitting, and the conversation with the approvers
        sheet.MapPost("/submit", (int year, int month, ClaimsPrincipal user, WorkflowService service) =>
            Run(() => service.SubmitAsync(Identities.EmployeeCode(user)!, year, month)));

        sheet.MapPost("/messages/{index:int}/answer", (int year, int month, int index, AnswerRequest request, ClaimsPrincipal user, WorkflowService service) =>
            Run(() => service.AnswerAsync(Identities.EmployeeCode(user)!, year, month, index, request.Answer)));

        sheet.MapGet("/revisions/{revision:int}", (int year, int month, int revision, ClaimsPrincipal user, WorkflowService service) =>
            Run(() => service.RevisionAsync(Identities.EmployeeCode(user)!, Identities.EmployeeCode(user)!, year, month, revision)));

        sheet.MapGet("/print/{format}", (int year, int month, string format, ClaimsPrincipal user, PrintService service) =>
            File(() => service.OwnAsync(Identities.EmployeeCode(user)!, year, month, format))).RequireSheetExport();
    }
}
