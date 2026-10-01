// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using InpremClockingApp.Data;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Areas.Identity.Pages.Account
{
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly ApplicationDbContext _db;
        private readonly EmailService _emailSender;

        public ForgotPasswordModel(UserManager<AppUser> userManager, ApplicationDbContext db, EmailService emailSender)
        {
            _userManager = userManager;
            _db = db;
            _emailSender = emailSender;
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
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                // AppUser.NormalizedEmail/NormalizedUserName is only unique per-tenant, not
                // globally (see ApplicationDbContext) - more than one organization's admin can
                // share this email address. UserManager.FindByEmailAsync can only ever return
                // one of them (an arbitrary pick), which would silently generate a reset token
                // for the WRONG account - the link in the email would either fail outright
                // (ResetPassword.cshtml.cs tries every candidate the code could belong to) or,
                // worse, let the recipient reset an account that isn't theirs. Instead: every
                // matching account gets its own token and its own labeled link in one email, so
                // the recipient - who does own that inbox either way - picks the right one.
                var candidates = await _db.Users.IgnoreQueryFilters()
                    .Where(u => u.NormalizedEmail == _userManager.NormalizeEmail(Input.Email))
                    .ToListAsync();

                if (candidates.Count == 0) //|| !(await _userManager.IsEmailConfirmedAsync(user)))
                {
                    // Don't reveal that the user does not exist or is not confirmed
                    return RedirectToPage("./ForgotPasswordConfirmation");
                }

                var tenantNames = await _db.Tenants.IgnoreQueryFilters()
                    .ToDictionaryAsync(t => t.Id, t => t.Name);

                var body = new StringBuilder();
                if (candidates.Count > 1)
                {
                    body.Append("<p>This email address is associated with more than one organization's account. Choose which one to reset:</p>");
                }

                foreach (var user in candidates)
                {
                    // For more information on how to enable account confirmation and password reset please
                    // visit https://go.microsoft.com/fwlink/?LinkID=532713
                    var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var callbackUrl = Url.Page(
                        "/Account/ResetPassword",
                        pageHandler: null,
                        values: new { area = "Identity", code },
                        protocol: Request.Scheme);

                    var label = user.TenantId.HasValue && tenantNames.TryGetValue(user.TenantId.Value, out var tenantName)
                        ? tenantName
                        : "Platform Administrator";

                    body.Append(candidates.Count > 1
                        ? $"<p>Reset password for <strong>{HtmlEncoder.Default.Encode(label)}</strong>: <a href='{HtmlEncoder.Default.Encode(callbackUrl!)}'>click here</a></p>"
                        : $"<p>Please reset your password by <a href='{HtmlEncoder.Default.Encode(callbackUrl!)}'>clicking here</a>.</p>");
                }

                await _emailSender.SendEmail(Input.Email, "Reset Password", body.ToString());

                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            return Page();
        }
    }
}
