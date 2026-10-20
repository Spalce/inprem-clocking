using System.Security.Claims;
using Inprem.Platform.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.StaffAlerts;

/// <summary>
/// The back-office editors (Manage Staff, Manage Volunteers) save by script; the consent box's own script
/// (in the included partial) sends the ticked box here after a save that succeeded (Phase 6 step 6.2).
/// Admins of the organization only, like the editors themselves.
/// </summary>
[ApiController]
[Route("api/staff-alerts/consent")]
[Authorize(Policy = "AdminOnly")]
public sealed class StaffConsentController : ControllerBase
{
    public sealed record ConsentRequest(string Kind, string? Email);

    private readonly StaffConsentRecorder _recorder;

    public StaffConsentController(StaffConsentRecorder recorder) => _recorder = recorder;

    [HttpPost]
    public async Task<IActionResult> PostAsync([FromBody] ConsentRequest request)
    {
        var kind = string.Equals(request.Kind, "volunteer", StringComparison.OrdinalIgnoreCase) ? PersonKind.Volunteer : PersonKind.Staff;
        var id = await _recorder.FindByEmailAsync(kind, request.Email, HttpContext.RequestAborted);
        if (id == null) return NotFound(new { recorded = false, reason = "The person was not found." });
        var reason = await _recorder.RecordAsync(kind, id.Value, ConsentSources.BackOffice, $"user:{User.FindFirstValue(ClaimTypes.NameIdentifier)}", HttpContext.RequestAborted);
        return Ok(new { recorded = reason == null, reason });
    }
}
