using Company.Api.Contracts;
using Company.Api.Services;

namespace Company.Api.Endpoints;

/// <summary>The one way an endpoint answers: the result, or a refusal in the form the desktop app shows as it is</summary>
public static class EndpointResults
{
    public static async Task<IResult> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (SheetException ex)
        {
            return Refusal(ex);
        }
    }

    public static async Task<IResult> File(Func<Task<PrintedFile>> action)
    {
        try
        {
            var file = await action();
            return Results.File(file.Bytes, file.ContentType, file.FileName);
        }
        catch (SheetException ex)
        {
            return Refusal(ex);
        }
    }

    /// <summary>The routes that give a timesheet as a file exist only while <c>Features:SheetExport</c> is on</summary>
    public static RouteHandlerBuilder RequireSheetExport(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
            context.HttpContext.RequestServices.GetRequiredService<CompanyFeatures>().SheetExport ? await next(context) : Results.NotFound());

    private static IResult Refusal(SheetException ex) => Results.Json(new ApiErrorDto(ex.Code, ex.Message), statusCode: ex.Status);
}
