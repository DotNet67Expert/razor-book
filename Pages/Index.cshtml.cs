using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PubSearchSite.Services;

namespace PubSearchSite.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly ILocationService _locationService;

    public IReadOnlyList<string> Locations { get; private set; } = Array.Empty<string>();
    public string ActiveTab => "LM";

    public IndexModel(ILogger<IndexModel> logger, ILocationService locationService)
    {
        _logger = logger;
        _locationService = locationService;
    }

    public async Task OnGetAsync()
    {
        Locations = await _locationService.GetLmSitesAsync(HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnGetDocumentsAsync(string site)
    {
        if (string.IsNullOrWhiteSpace(site))
        {
            return new JsonResult(new { keyDocuments = Array.Empty<DocumentItem>(), siteDocuments = Array.Empty<DocumentItem>() });
        }

        var result = await _locationService.GetLmDocumentsAsync(site, HttpContext.RequestAborted);
        return new JsonResult(new
        {
            keyDocuments = result.KeyDocuments,
            siteDocuments = result.SiteDocuments
        });
    }
}
