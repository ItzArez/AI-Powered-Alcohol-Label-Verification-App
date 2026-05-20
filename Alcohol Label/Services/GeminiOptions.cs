namespace Alcohol_Label.Services;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemini-2.5-flash-lite";
    public string EndpointBaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public int MaxInlineFileBytes { get; set; } = 20 * 1024 * 1024;
}
