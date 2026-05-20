using Alcohol_Label.Models;

namespace Alcohol_Label.Services;

public interface IOcrService
{
    Task<LabelApplication> ExtractLabelApplicationAsync(IFormFile? file, string? pastedText, CancellationToken cancellationToken = default);
}
