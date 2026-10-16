using System.Diagnostics;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Nexus.Application.Common;
using Nexus.Domain.Common;

namespace Nexus.Api.Web;

/// <summary>
/// Maps exceptions to RFC 9457 problem responses with a stable <c>code</c> extension. Unexpected errors are logged
/// with the correlation id and answered with a generic message: stack traces and internal details never reach the
/// client, in any environment.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away; nothing useful to answer.
            return true;
        }

        var problem = exception switch
        {
            ValidationException validation => CreateValidationProblem(validation),
            NotFoundException notFound => Problem(StatusCodes.Status404NotFound, notFound.Code, notFound.Message),
            UnauthenticatedException unauthenticated => Problem(StatusCodes.Status401Unauthorized, unauthenticated.Code,
                unauthenticated.Message),
            ForbiddenException forbidden => Problem(StatusCodes.Status403Forbidden, forbidden.Code, forbidden.Message),
            ConflictException conflict => WithDetails(Problem(StatusCodes.Status409Conflict, conflict.Code, conflict.Message),
                conflict.Details),
            DomainException domain => Problem(StatusCodes.Status422UnprocessableEntity, domain.Code, domain.Message),
            BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } => Problem(
                StatusCodes.Status413PayloadTooLarge, "payload_too_large", "La solicitud supera el tamaño máximo permitido."),
            BadHttpRequestException badRequest => Problem(badRequest.StatusCode, "bad_request", "La solicitud no es válida."),
            _ => null,
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method,
                httpContext.Request.Path);
            problem = Problem(StatusCodes.Status500InternalServerError, "internal_error",
                "Ha ocurrido un error inesperado. Inténtalo de nuevo más tarde.");
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            // Deliberately not passing the exception: the developer page would otherwise serialize it.
        });
    }

    private static ProblemDetails WithDetails(ProblemDetails problem, IReadOnlyDictionary<string, object?> details)
    {
        foreach (var (key, value) in details)
        {
            problem.Extensions[key] = value;
        }

        return problem;
    }

    internal static ProblemDetails Problem(int status, string code, string detail) => new()
    {
        Status = status,
        Title = TitleFor(status),
        Detail = detail,
        Extensions = { ["code"] = code },
    };

    /// <summary>
    /// Adds the correlation id and a default <c>code</c> to every problem response, including the ones produced by
    /// the framework (404 for unknown routes, 405, 415…).
    /// </summary>
    internal static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Extensions["traceId"] = Correlation.Id(context.HttpContext);
        problem.Extensions.TryAdd("code", DefaultCode(problem.Status));
        problem.Title ??= TitleFor(problem.Status ?? StatusCodes.Status500InternalServerError);
        // Framework problems carry a "type" link to the RFC; it adds nothing for API clients.
        problem.Type = null;
        problem.Instance = null;
    }

    private static ValidationProblemDetails CreateValidationProblem(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => CamelCasePath(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = TitleFor(StatusCodes.Status400BadRequest),
            Detail = "Revisa los datos enviados.",
            Extensions = { ["code"] = "validation_failed" },
        };
    }

    /// <summary>"Lines[0].ProductId" → "lines[0].productId", matching the JSON the client sent.</summary>
    private static string CamelCasePath(string path) =>
        string.Join('.', path.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));

    private static string DefaultCode(int? status) => status switch
    {
        400 => "bad_request",
        401 => "unauthorized",
        403 => "forbidden",
        404 => "not_found",
        405 => "method_not_allowed",
        409 => "conflict",
        413 => "payload_too_large",
        415 => "unsupported_media_type",
        422 => "business_rule_violated",
        429 => "rate_limited",
        _ => "internal_error",
    };

    private static string TitleFor(int status) => status switch
    {
        400 => "Solicitud no válida",
        401 => "No autenticado",
        403 => "Acceso denegado",
        404 => "No encontrado",
        405 => "Método no permitido",
        409 => "Conflicto",
        413 => "Contenido demasiado grande",
        415 => "Tipo de contenido no admitido",
        422 => "Regla de negocio incumplida",
        429 => "Demasiadas solicitudes",
        503 => "Servicio no disponible",
        _ => "Error del servidor",
    };
}

internal static class Correlation
{
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>The W3C trace id of the request: the same value appears in logs, problem bodies and the response header.</summary>
    public static string Id(HttpContext context) =>
        Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
}
