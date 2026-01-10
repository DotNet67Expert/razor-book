using Microsoft.AspNetCore.Mvc.RazorPages;
using PubSearchSite.Services;

namespace PubSearchSite.Pages;

public class CerclaArPdModel : PageModel
{
    private readonly ILogger<CerclaArPdModel> _logger;
    private readonly ILocationService _locationService;

    public IReadOnlyList<string> Locations { get; private set; } = Array.Empty<string>();
    public string ActiveTab => "CERCLA";

    public CerclaArPdModel(ILogger<CerclaArPdModel> logger, ILocationService locationService)
    {
        _logger = logger;
        _locationService = locationService;
    }

    public async Task OnGetAsync()
    {
        Locations = await _locationService.GetCerclaSitesAsync(HttpContext.RequestAborted);
    }
}

