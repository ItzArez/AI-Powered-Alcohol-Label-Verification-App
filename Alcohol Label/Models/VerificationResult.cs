namespace Alcohol_Label.Models;

public class VerificationResult
{
    public LabelApplication Application { get; set; } = new();
    public ExpectedLabelValues ExpectedValues { get; set; } = new();
    public List<FieldCheckResult> Checks { get; set; } = [];
    public DateTime VerifiedAtUtc { get; set; } = DateTime.UtcNow;

    public bool Passed => Checks.Count > 0 && Checks.All(check => check.Passed);
    public int PassedCount => Checks.Count(check => check.Passed);
    public int FailedCount => Checks.Count(check => !check.Passed);
}
