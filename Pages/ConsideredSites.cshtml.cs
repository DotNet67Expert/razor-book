using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PubSearchSite.Services;

namespace PubSearchSite.Pages;

public class ConsideredSitesModel : PageModel
{
    private readonly ILogger<ConsideredSitesModel> _logger;
    private readonly ILocationService _locationService;

    public IReadOnlyList<string> Locations { get; private set; } = Array.Empty<string>();
    public string ActiveTab => "Considered";

    public ConsideredSitesModel(ILogger<ConsideredSitesModel> logger, ILocationService locationService)
    {
        _logger = logger;
        _locationService = locationService;
    }

    public async Task OnGetAsync()
    {
        Locations = await _locationService.GetConsideredSitesAsync(HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnGetSiteDetailsAsync(string filterName)
    {
        if (string.IsNullOrWhiteSpace(filterName))
        {
            return new JsonResult((object?)null);
        }

        var details = await _locationService.GetConsideredSiteDetailsAsync(filterName, HttpContext.RequestAborted);
        return new JsonResult(details);
    }

    public async Task<IActionResult> OnGetSiteDocumentsAsync(string filterName)
    {
        if (string.IsNullOrWhiteSpace(filterName))
        {
            return new JsonResult(Array.Empty<object>());
        }

        var documents = await _locationService.GetConsideredSiteDocumentsAsync(filterName, HttpContext.RequestAborted);
        return new JsonResult(documents);
    }
}

