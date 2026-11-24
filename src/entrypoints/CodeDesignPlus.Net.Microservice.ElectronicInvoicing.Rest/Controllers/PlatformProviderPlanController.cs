using CodeDesignPlus.Net.Exceptions.Guards;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.Quota;
using NodaTime.Text;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;

/// <summary>
/// The plan contracted with the provider, shared by every issuer: how a calendar month stands, with the documents it
/// includes, the ones used, the overage and what the overage is estimated to cost.
/// </summary>
/// <remarks>
/// Its own controller so the RBAC grants it apart, to whoever runs the platform. It does not depend on the tenant of the
/// session, and this service does not check roles.
/// </remarks>
/// <param name="mediator">Mediator that sends the queries.</param>
[Route("api/[controller]")]
[ApiController]
public class PlatformProviderPlanController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// How a calendar month of the plan stands, measured now.
    /// </summary>
    /// <param name="month">Any day of the month, written yyyy-MM-dd; the current month in the time zone of the plan when it is not given.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>200 with the month; 204 when the contract does not cover it; 400 when the day is not a calendar date.</returns>
    [HttpGet]
    public async Task<IActionResult> GetMonth([FromQuery] string? month, CancellationToken cancellationToken)
    {
        LocalDate? day = null;

        if (!string.IsNullOrWhiteSpace(month))
        {
            var parsed = LocalDatePattern.Iso.Parse(month);

            InfrastructureGuard.IsFalse(parsed.Success, Infrastructure.Errors.InvalidDate);

            day = parsed.Value;
        }

        var measured = await mediator.Send(new GetProviderPlanMonthQuery(day), cancellationToken);

        if (measured is null)
        {
            return NoContent();
        }

        return Ok(measured);
    }
}
