using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using Inprem.Platform.Application.PublicApi;
using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace InpremClockingApp.PublicApi;

public sealed record VolunteerRegistrationRequest(
    int? TenantId,
    string? EmailAddress,
    string? FirstName,
    string? LastName,
    string? ZipCode,
    char? Gender,
    string? PhoneNumber,
    string? Address,
    string? VolunteerCategory,
    string? MandateType,
    string? InstitutionName,
    string? PlaceOfWork,
    string? ContactPerson,
    string? ContactPersonPosition,
    string? ContactPersonEmail,
    string? ContactPersonPhone,
    bool? Consent,
    string? CaptchaToken);

public enum VolunteerRegistrationError
{
    Validation,
    Captcha,
    TenantNotFound,
    TenantNotActive,
    DuplicateEmail,
    DuplicatePhone
}

public sealed record VolunteerRegistrationResult(
    Volunteer? Volunteer,
    VolunteerRegistrationError? Error,
    IDictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    public bool Succeeded => Error == null;

    public static VolunteerRegistrationResult Fail(VolunteerRegistrationError error, string message,
        IDictionary<string, string[]>? errors = null) => new(null, error, errors, message);
}

public sealed class VolunteerRegistrationService
{
    private readonly ApplicationDbContext _db;
    private readonly ICaptchaVerifier _captcha;

    public VolunteerRegistrationService(ApplicationDbContext db, ICaptchaVerifier captcha)
    {
        _db = db;
        _captcha = captcha;
    }

    public async Task<VolunteerRegistrationResult> RegisterAsync(
        VolunteerRegistrationRequest request, string? remoteIp, CancellationToken ct = default)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return VolunteerRegistrationResult.Fail(VolunteerRegistrationError.Validation,
                "One or more fields are invalid.", errors);

        if (!await _captcha.VerifyAsync(request.CaptchaToken, remoteIp, ct).ConfigureAwait(false))
            return VolunteerRegistrationResult.Fail(VolunteerRegistrationError.Captcha,
                "The CAPTCHA check failed. Please try again.");

        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TenantId!.Value, ct)
            .ConfigureAwait(false);

        if (tenant == null)
            return VolunteerRegistrationResult.Fail(VolunteerRegistrationError.TenantNotFound,
                "The organization could not be found.");

        if (!tenant.IsActive)
            return VolunteerRegistrationResult.Fail(VolunteerRegistrationError.TenantNotActive,
                "The organization is not accepting registrations.");

        var email = request.EmailAddress!.Trim();
        var phone = request.PhoneNumber!.Trim();
        var duplicate = await _db.Volunteers.IgnoreQueryFilters().AsNoTracking()
            .Where(v => v.TenantId == request.TenantId.Value &&
                (v.EmailAddress == email || v.PhoneNumber == phone))
            .Select(v => new { v.EmailAddress, v.PhoneNumber })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (duplicate != null)
            return duplicate.EmailAddress == email
                ? VolunteerRegistrationResult.Fail(VolunteerRegistrationError.DuplicateEmail,
                    "A volunteer with this email address is already registered.")
                : VolunteerRegistrationResult.Fail(VolunteerRegistrationError.DuplicatePhone,
                    "A volunteer with this phone number is already registered.");

        var volunteer = new Volunteer
        {
            TenantId = request.TenantId.Value,
            EmailAddress = email,
            FirstName = request.FirstName!.Trim(),
            LastName = request.LastName!.Trim(),
            ZipCode = request.ZipCode!.Trim(),
            Gender = request.Gender!.Value,
            PhoneNumber = phone,
            Address = Blank(request.Address),
            VolunteerCategory = Blank(request.VolunteerCategory),
            MandateType = Blank(request.MandateType),
            InstitutionName = Blank(request.InstitutionName),
            PlaceOfWork = Blank(request.PlaceOfWork),
            ContactPerson = Blank(request.ContactPerson),
            ContactPersonPosition = Blank(request.ContactPersonPosition),
            ContactPersonEmail = Blank(request.ContactPersonEmail),
            ContactPersonPhone = Blank(request.ContactPersonPhone),
            CreatedAt = DateTime.UtcNow,
            Type = "Volunteer",
            ApprovalStatus = VolunteerApprovalStatus.Pending
        };

        try
        {
            _db.Volunteers.Add(volunteer);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsDuplicateEmailViolation(ex))
        {
            return VolunteerRegistrationResult.Fail(VolunteerRegistrationError.DuplicateEmail,
                "A volunteer with this email address is already registered.");
        }

        return new VolunteerRegistrationResult(volunteer, null);
    }

    private static Dictionary<string, string[]> Validate(VolunteerRegistrationRequest request)
    {
        var errors = new ModelStateDictionary();
        if (request.TenantId is null or <= 0)
            errors.AddModelError("tenantId", "A valid organization is required.");
        if (request.Consent != true)
            errors.AddModelError("consent", "Consent is required.");
        if (request.Gender is null or '\0')
            errors.AddModelError("gender", "Gender is required.");
        if (string.IsNullOrWhiteSpace(request.CaptchaToken))
            errors.AddModelError("captchaToken", "The CAPTCHA answer is required.");
        if (!string.IsNullOrWhiteSpace(request.EmailAddress) && !IsValidEmail(request.EmailAddress))
            errors.AddModelError("emailAddress", "Enter a valid email address.");

        var volunteer = new Volunteer
        {
            EmailAddress = request.EmailAddress ?? string.Empty,
            FirstName = request.FirstName,
            LastName = request.LastName,
            ZipCode = request.ZipCode,
            Gender = request.Gender ?? '\0',
            PhoneNumber = request.PhoneNumber,
            Address = request.Address,
            VolunteerCategory = request.VolunteerCategory,
            MandateType = request.MandateType,
            InstitutionName = request.InstitutionName,
            PlaceOfWork = request.PlaceOfWork,
            ContactPerson = request.ContactPerson,
            ContactPersonPosition = request.ContactPersonPosition,
            ContactPersonEmail = request.ContactPersonEmail,
            ContactPersonPhone = request.ContactPersonPhone
        };

        var validationContext = new ValidationContext(volunteer);
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(volunteer, validationContext, validationResults, validateAllProperties: true);
        foreach (var result in validationResults)
        {
            var members = result.MemberNames.Any() ? result.MemberNames : new[] { "body" };
            foreach (var member in members)
                errors.AddModelError(ToCamelCase(member), result.ErrorMessage ?? "The value is invalid.");
        }

        VolunteerCategoryValidation.Validate(volunteer, errors);
        return errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Errors.Select(error => error.ErrorMessage ?? "The value is invalid.").ToArray());
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value.Trim());
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsDuplicateEmailViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627) &&
        sql.Message.Contains("IX_Volunteers_TenantId_EmailAddress", StringComparison.OrdinalIgnoreCase);

    private static string ToCamelCase(string value) => string.IsNullOrEmpty(value)
        ? value
        : char.ToLowerInvariant(value[0]) + value[1..];
}
