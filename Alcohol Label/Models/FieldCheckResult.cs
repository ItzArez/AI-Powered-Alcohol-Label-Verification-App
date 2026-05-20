namespace Alcohol_Label.Models;

public class FieldCheckResult
{
    public string FieldName { get; set; } = string.Empty;
    public string ExtractedValue { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Message { get; set; } = string.Empty;
}
