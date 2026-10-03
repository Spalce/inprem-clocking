// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace InpremClockingApp.Areas.Identity.Pages.Account
{
    // AddDefaultIdentity() applies AuthorizeAreaPage("Identity", "/Account/Logout") by its own
    // convention, so a Logout request arriving after the auth cookie is already gone (e.g. a
    // concurrent tab logged out first, sharing the same cookie) gets challenged before OnPost
    // below ever runs - the framework's own challenge handler then redirects to Login using the
    // Logout URL itself as ReturnUrl, which Login.cshtml's kiosk/Back Office look-up doesn't
    // recognize. SignOutAsync() is a harmless no-op when there's no session to clear, so it's
    // safe to let this run unauthenticated and keep OnPost in control of where the user lands.
    //
    // IgnoreAntiforgeryToken is needed alongside AllowAnonymous for the same scenario: the
    // antiforgery token embedded in the form was minted while the page's tab was still
    // authenticated, so by the time a now-anonymous POST reaches validation (cookie cleared by
    // the other tab) the token's bound identity no longer matches and it's rejected with a 400 -
    // before OnPost ever runs. Logout has no state worth CSRF-protecting (worst case a forged
    // request just signs someone out), so skipping the check here is the standard trade-off.
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly ILogger<LogoutModel> _logger;

        public LogoutModel(SignInManager<AppUser> signInManager, ILogger<LogoutModel> logger)
        {
            _signInManager = signInManager;
            _logger = logger;
        }

        public async Task<IActionResult> OnGet(string returnUrl = null)
        {
            await _signInManager.SignOutAsync();
            return RedirectToPage("Login", new { returnUrl });
        }

        public async Task<IActionResult> OnPost(string returnUrl = null)
        {
            await _signInManager.SignOutAsync();
            return RedirectToPage("Login", new { returnUrl });
        }
    }
}
