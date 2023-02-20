using InpremClockingApp.Models;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages;

public class Settings : PageModel
{
    private readonly SettingService _service;

    public Settings(SettingService service)
    {
        _service = service;
    }

    public Setting? Model = new();
    
    public async Task<IActionResult> OnGet()
    {
        var record = await _service.GetByIdAsync(1);
        if (record != null!)
            Model = record;
        return Page();
    }
    
    public async Task<IActionResult> OnPostAsync([FromForm] Setting model)
    {
        //var record = await _service.GetAllAsync();
        if (model!.Id != 0)
        {
            await _service.Update(model!);
        }
        else
        {
            await _service.Create(model!);
        }
        Console.WriteLine($"{model.Duration} {model.Id} {model.Name}");
        return RedirectToPage("./Settings");
    }
}
