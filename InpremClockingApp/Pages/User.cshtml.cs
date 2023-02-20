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

    public async Task<IActionResult> OnGetAsync()
    {
        var model = await _service.GetAll().ConfigureAwait(true);
        if (model != null!)
            Users = model;
        return Page();
    }
}
