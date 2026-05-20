using Alcohol_Label.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Alcohol_Label.Pages;

public class ResultsModel : PageModel
{
    public VerificationResult? Result { get; private set; }

    public void OnGet()
    {
        var json = TempData["VerificationResult"] as string;
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        Result = System.Text.Json.JsonSerializer.Deserialize<VerificationResult>(json);
        TempData.Keep("VerificationResult");
    }
}
