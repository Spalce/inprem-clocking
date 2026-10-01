// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InpremClockingApp.Data;
using InpremClockingApp.Helpers;
using InpremClockingApp.Services;

namespace InpremClockingApp.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(SignInManager<AppUser> signInManager, ApplicationDbContext db, ILogger<LoginModel> logger)
        {
            _signInManager = signInManager;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string ErrorMessage { get; set; }

        [TempData] public string Return { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Display(Name = "Remember me?")]
            public bool RememberMe { get; set; }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            // Deliberately NOT defaulted to ~/VolunteerAttendance here (unlike before) - doing
            // so meant the view's form always embedded a concrete, already-resolved destination
            // via asp-route-returnUrl, so OnPostAsync's own "no real destination" fallback logic
            // below never actually saw a null/empty returnUrl for the single most common case:
            // visiting this page directly with no query string at all (only the rarer
            // "challenged from root /" case, where returnUrl really is the literal "/", ever
            // reached it). Leaving this null when nothing was requested lets the tag helper omit
            // the query param entirely, so OnPostAsync's fallback - including the SuperAdmin
            // override - runs for both cases alike.
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = Return = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            // Fall back to a sensible default only when there's no real destination to return to
            // (e.g. landing on Login directly, or being challenged from the root "/" page).
            // Otherwise honor whatever protected page originally triggered the login challenge.
            var usedKioskFallback = false;
            if (string.IsNullOrEmpty(returnUrl) || returnUrl == "/")
            {
                if (!string.IsNullOrEmpty(Return) && Return != "/" && Url.IsLocalUrl(Return))
                {
                    returnUrl = Return;
                }
                else
                {
                    returnUrl = Url.Content("~/VolunteerAttendance");
                    usedKioskFallback = true;
                }
            }

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                // AppUser.NormalizedUserName is only unique per-tenant, not globally (see
                // ApplicationDbContext) - two different organizations' admins can share a
                // username/email. Resolves which specific account "Input.Email" means here,
                // since plain SignInManager.PasswordSignInAsync(string userName, ...) would
                // otherwise pick an arbitrary one of the matching rows (an unordered TOP(1)
                // query under the hood) with no way to know which tenant was actually intended.
                var candidate = await ResolveLoginCandidateAsync(Input.Email, Input.Password).ConfigureAwait(false);

                // This doesn't count login failures towards account lockout
                // To enable password failures to trigger account lockout, set lockoutOnFailure: true
                var result = candidate == null
                    ? Microsoft.AspNetCore.Identity.SignInResult.Failed
                    : await _signInManager.PasswordSignInAsync(candidate, Input.Password, Input.RememberMe, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    // The kiosk fallback above predates the SuperAdmin role (multi-tenancy.md) -
                    // a SuperAdmin has no TenantId, so VolunteerAttendance is never a meaningful
                    // destination for one. Only overrides the generic fallback, never an explicit
                    // destination (e.g. being challenged from /Platform/Tenants itself already
                    // produces that exact returnUrl above, untouched by this).
                    if (usedKioskFallback && await _signInManager.UserManager.IsInRoleAsync(candidate, IdentitySeeder.SuperAdminRole))
                    {
                        returnUrl = Url.Content("~/Platform/Tenants");
                    }

                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }
                if (result.IsLockedOut)
                {
                    return RedirectToPage("./Lockout");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return Page();
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }

        // Resolves which account "userName" refers to now that it's only unique per-tenant
        // (TenantAwareUserValidator/ApplicationDbContext). Zero or one match behaves exactly
        // like a plain FindByNameAsync lookup - the only case that changes is more than one
        // tenant sharing a username, where the supplied password is what disambiguates which
        // account was meant (CheckPasswordAsync here doesn't touch lockout state - only the
        // eventual PasswordSignInAsync call against the resolved candidate does that, same as
        // for any other login attempt). If no candidate's password matches, falls through to a
        // deterministic (lowest Id) candidate so PasswordSignInAsync still runs its normal
        // failure/lockout handling against a real account, instead of silently skipping it the
        // way returning null for "account doesn't exist" would.
        private async Task<AppUser> ResolveLoginCandidateAsync(string userName, string password)
        {
            var normalized = _signInManager.UserManager.NormalizeName(userName);
            var candidates = await _db.Users.IgnoreQueryFilters()
                .Where(u => u.NormalizedUserName == normalized)
                .OrderBy(u => u.Id)
                .ToListAsync().ConfigureAwait(false);

            if (candidates.Count <= 1) return candidates.FirstOrDefault();

            foreach (var account in candidates)
            {
                if (await _signInManager.UserManager.CheckPasswordAsync(account, password).ConfigureAwait(false))
                {
                    return account;
                }
            }

            return candidates[0];
        }
    }
}
