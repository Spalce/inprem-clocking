using System.Security.Cryptography;
using System.Text;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;
using InpremClockingApp.Helpers;
using InpremClockingApp.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;

[Authorize(Policy = "AdminOnly")]
public sealed class TestProvisionModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly TestAccountProvisioner _provisioner;

    public TestProvisionModel(IConfiguration configuration, IWebHostEnvironment environment, TestAccountProvisioner provisioner)
    {
        _configuration = configuration;
        _environment = environment;
        _provisioner = provisioner;
    }

    [BindProperty] public string AccessSecret { get; set; } = "";
    [BindProperty] public string AccountType { get; set; } = "provider";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public ProviderRole ProviderRole { get; set; } = ProviderRole.Support;
    [BindProperty] public string OrganizationName { get; set; } = "";
    [BindProperty] public string OrganizationSlug { get; set; } = "";
    [BindProperty] public string FirstName { get; set; } = "";
    [BindProperty] public string LastName { get; set; } = "";
    [BindProperty] public int TenantId { get; set; }

    public string? Error { get; private set; }
    public string? Success { get; private set; }
    public bool IsAvailable => _configuration.GetValue<bool>("AccountProvisioning:Enabled")
        && (!_environment.IsProduction() || _configuration.GetValue<bool>("AccountProvisioning:AllowInProduction"));

    public IActionResult OnGet() => IsAvailable ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!IsAvailable) return NotFound();
        if (!SecretMatches(AccessSecret, _configuration["AccountProvisioning:Secret"]))
        {
            Error = "The provisioning secret is not valid.";
            return Page();
        }

        try
        {
            var actor = $"test-provisioner:{User.Identity?.Name}";
            ProvisionResult result;
            if (AccountType == "provider")
            {
                result = await _provisioner.CreateProviderAsync(Email, Password, ProviderRole, HttpContext.RequestAborted);
            }
            else if (AccountType == "owner")
            {
                var slug = string.IsNullOrWhiteSpace(OrganizationSlug) ? Slug(OrganizationName) : OrganizationSlug.Trim().ToLowerInvariant();
                result = await _provisioner.CreateOrganizationOwnerAsync(
                    new NewOrganization(OrganizationName, slug, "America/New_York", null, null, FirstName, LastName,
                        Email, null, null, null, BillingMode.Free, 0), Password, actor, HttpContext.RequestAborted);
            }
            else if (AccountType == "admin")
            {
                result = await _provisioner.CreateOrganizationAdminAsync(TenantId, Email, Password, FirstName, LastName, actor, HttpContext.RequestAborted);
            }
            else
            {
                throw new InvalidOperationException("Choose a supported account type.");
            }

            Success = result.Message + " Email verification and invitation email were bypassed for this test account.";
            ModelState.Clear();
            return Page();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Error = ex.Message;
            return Page();
        }
    }

    private static bool SecretMatches(string supplied, string? configured)
    {
        if (string.IsNullOrWhiteSpace(supplied) || string.IsNullOrWhiteSpace(configured)) return false;
        var left = Encoding.UTF8.GetBytes(supplied);
        var right = Encoding.UTF8.GetBytes(configured);
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string Slug(string value) => new string((value ?? "org").Trim().ToLowerInvariant()
        .Select(c => char.IsLetterOrDigit(c) ? c : '-')
        .ToArray()).Trim('-');
}
