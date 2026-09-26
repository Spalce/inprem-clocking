using System.ComponentModel.DataAnnotations;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;

public class User : PageModel
{
    private readonly AuthService _service;

    public User(AuthService service)
    {
        _service = service;
    }

    public IEnumerable<AppUser>? Users { get; set; }

    // Named "pageNumber" rather than "page" because "page" is a reserved Razor Pages route
    // value (the page's own path) - a query string "page" is intercepted by route-value model
    // binding before it ever reaches a same-named property.
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    [BindProperty]
    public CreateAdminInput Input { get; set; } = new();

    public class CreateAdminInput
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadUsersAsync().ConfigureAwait(true);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadUsersAsync().ConfigureAwait(true);
            return Page();
        }

        var result = await _service.CreateAdmin(Input.Email, Input.FirstName, Input.LastName, Input.Password).ConfigureAwait(true);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            await LoadUsersAsync().ConfigureAwait(true);
            return Page();
        }

        TempData["Message"] = $"Admin account created for {Input.Email}.";
        return RedirectToPage("./User");
    }

    private async Task LoadUsersAsync()
    {
        var result = await _service.GetPaged(PageNumber, PageSize).ConfigureAwait(true);
        Users = result.Items;

        ViewData["TotalCount"] = result.TotalCount;
        ViewData["Page"] = result.Page;
        ViewData["PageSize"] = result.PageSize;
        ViewData["TotalPages"] = result.TotalPages;
    }
}
