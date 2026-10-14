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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Inprem.Platform.Application.Memberships;
using InpremClockingApp.Helpers;
using InpremClockingApp.Services;

namespace InpremClockingApp.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(SignInManager<AppUser> signInManager, ILogger<LoginModel> logger)
        {
            _signInManager = signInManager;
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
            if (string.IsNullOrEmpty(returnUrl) || returnUrl == "/")
            {
                if (!string.IsNullOrEmpty(Return) && Return != "/" && Url.IsLocalUrl(Return))
                {
                    returnUrl = Return;
                }
                else
                {
                    returnUrl = Url.Content("~/VolunteerAttendance");
                }
            }

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                // C2-3 (multi-tenancy Phase 0 step 0.4): an email is one account platform-wide
                // (unique index, migration M3), so the standard sign-in by user name is exact again;
                // the organization comes from the account's membership (claims factory, and the
                // "Choose organization" page for people in more than one). Failed attempts now
                // count towards lockout (C3-7: 5 attempts, 15 minutes - see AccountHosting).
                var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    // Multi-tenancy Phase 1 step 1.7: the old platform-operator pages (/Platform) were
                    // replaced by the Provider Console, so a SuperAdmin account without any
                    // active organization membership has nothing on the App site: it is signed out again with a pointer.
                    var signedIn = await _signInManager.UserManager.FindByNameAsync(Input.Email);
                    if (signedIn != null && await _signInManager.UserManager.IsInRoleAsync(signedIn, IdentitySeeder.SuperAdminRole)
                        && (await HttpContext.RequestServices.GetRequiredService<IMembershipService>().GetForSignInAsync(signedIn.Id, signedIn.TenantId)).Count == 0)
                    {
                        await _signInManager.SignOutAsync();
                        ModelState.AddModelError(string.Empty, "This is a platform-operator account. Platform operations moved to the Provider Console (the admin site).");
                        return Page();
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
    }
}
