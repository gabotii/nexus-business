using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.Application.Common;
using Nexus.Domain.Common;

namespace Nexus.IntegrationTests;

/// <summary>
/// Registered only by <see cref="ApiFactory"/> (application part from the test assembly): throws each kind of
/// exception so the error contract can be verified end to end.
/// </summary>
[ApiController]
[Route("api/v1/test-only/errors")]
[AllowAnonymous]
public sealed class TestOnlyController : ControllerBase
{
    public const string SecretDetail = "connection string Password=hunter2 at Nexus.Internal.Thing";

    [HttpGet("{kind}")]
    public IActionResult Throw(string kind) => kind switch
    {
        "unhandled" => throw new InvalidOperationException(SecretDetail),
        "domain" => throw new DomainException("insufficient_stock", "No hay stock suficiente."),
        "not-found" => throw new NotFoundException(),
        "forbidden" => throw new ForbiddenException("permission_required", "No tienes permiso."),
        "conflict" => throw new ConflictException("concurrency_conflict", "Otro usuario modificó el registro."),
        "validation" => throw new ValidationException([new ValidationFailure("Lines[0].Quantity", "Debe ser positiva.")]),
        _ => Ok(),
    };
}
