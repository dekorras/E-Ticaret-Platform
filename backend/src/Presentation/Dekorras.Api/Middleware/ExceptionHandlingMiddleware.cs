using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Dekorras.Api.Middleware;

/// <summary>Kabul kriteri: hatalar RFC 7807 (Problem Details) formatında dönmelidir (bkz. plan §11).</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Doğrulama Hatası",
                string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)));
        }
        catch (KeyNotFoundException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status404NotFound, "Bulunamadı", ex.Message);
        }
        catch (Dekorras.Domain.Common.DomainException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, "İş Kuralı İhlali", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "İşlenmeyen hata");
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "Sunucu Hatası", "Beklenmeyen bir hata oluştu.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        await context.Response.WriteAsJsonAsync(problemDetails);
    }
}
