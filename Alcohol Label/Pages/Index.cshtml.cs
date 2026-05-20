using Alcohol_Label.Models;
using Alcohol_Label.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Alcohol_Label.Pages;

public class IndexModel : PageModel
{
    private readonly IOcrService _ocrService;
    private readonly ILabelVerificationService _verificationService;

    public IndexModel(IOcrService ocrService, ILabelVerificationService verificationService)
    {
        _ocrService = ocrService;
        _verificationService = verificationService;
    }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    [BindProperty]
    public string? PastedText { get; set; }

    [BindProperty]
    public ExpectedLabelValues ExpectedValues { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if ((Upload is null || Upload.Length == 0) && string.IsNullOrWhiteSpace(PastedText))
        {
            ErrorMessage = "Upload a file or paste OCR text before verifying.";
            return Page();
        }

        try
        {
            var application = await _ocrService.ExtractLabelApplicationAsync(Upload, PastedText, cancellationToken);
            var result = _verificationService.Verify(application, ExpectedValues);

            TempData["VerificationResult"] = System.Text.Json.JsonSerializer.Serialize(result);
            return RedirectToPage("/Results");
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
            return Page();
        }
    }
}
