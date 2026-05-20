using Alcohol_Label.Models;

namespace Alcohol_Label.Services;

public interface ILabelVerificationService
{
    VerificationResult Verify(LabelApplication application, ExpectedLabelValues? expectedValues = null);
}
