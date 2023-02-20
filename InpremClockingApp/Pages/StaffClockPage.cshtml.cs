using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;

public class StaffClockPage : PageModel
{
    public void OnGet(long id)
    {
        ViewData["id"] = id;
    }
}
