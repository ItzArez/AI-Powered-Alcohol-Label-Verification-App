using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alcohol_Label.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Alcohol_Label.Services;

public class OcrService : IOcrService
{
    private const string GeminiPrompt = """
        You are reading an alcohol beverage label image.
        Extract the visible label information into the requested JSON schema.
        If a field is not visible, use an empty string.
        The rawText field should include all readable text from the label.
        The containsSulfites field should be "Yes" if the label says contains sulfites or sulphites, "No" if clearly absent, or empty if unknown.
        """;

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly GeminiOptions _options;

    public OcrService(HttpClient httpClient, IMemoryCache cache, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<LabelApplication> ExtractLabelApplicationAsync(
        IFormFile? file,
        string? pastedText,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(pastedText))
        {
            return ParseLabelText(pastedText);
        }

        if (file is null || file.Length == 0)
        {
            return ParseLabelText(string.Empty);
        }

        if (IsTextUpload(file))
        {
            var rawText = await ReadUploadedTextAsync(file, cancellationToken);
            return ParseLabelText(rawText);
        }

        return await ExtractWithGeminiAsync(file, cancellationToken);
    }

    private static async Task<string> ReadUploadedTextAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return string.Empty;
        }

        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private async Task<LabelApplication> ExtractWithGeminiAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is missing. Set Gemini:ApiKey with dotnet user-secrets or an environment variable.");
        }

        if (file.Length > _options.MaxInlineFileBytes)
        {
            throw new InvalidOperationException(
                $"The uploaded file is too large for inline Gemini processing. Maximum size is {_options.MaxInlineFileBytes / 1024 / 1024} MB.");
        }

        var mimeType = GetMimeType(file);
        if (!IsGeminiSupportedUpload(mimeType))
        {
            throw new InvalidOperationException("Upload a JPG, PNG, WebP, PDF, TXT, or CSV file.");
        }

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        var fileBytes = memoryStream.ToArray();
        var cacheKey = CreateCacheKey(fileBytes, mimeType);

        if (_cache.TryGetValue(cacheKey, out LabelApplication? cachedApplication) && cachedApplication is not null)
        {
            return CloneLabelApplication(cachedApplication);
        }

        var base64File = Convert.ToBase64String(fileBytes);

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new
                        {
                            inline_data = new
                            {
                                mime_type = mimeType,
                                data = base64File
                            }
                        },
                        new
                        {
                            text = GeminiPrompt
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseJsonSchema = CreateLabelResponseSchema(),
                temperature = 0,
                maxOutputTokens = 1200,
                thinkingConfig = new
                {
                    thinkingBudget = 0
                }
            }
        };

        var endpoint = $"{_options.EndpointBaseUrl.TrimEnd('/')}/models/{Uri.EscapeDataString(_options.Model)}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("x-goog-api-key", _options.ApiKey);
        request.Content = JsonContent.Create(payload);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildGeminiErrorMessage(response.StatusCode, response.ReasonPhrase, responseBody));
        }

        var geminiText = ExtractGeminiText(responseBody);
        var application = ParseGeminiLabelApplication(geminiText);

        _cache.Set(
            cacheKey,
            CloneLabelApplication(application),
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(2),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(8),
                Size = 1
            });

        return application;
    }

    private string CreateCacheKey(byte[] fileBytes, string mimeType)
    {
        var hash = Convert.ToHexString(SHA256.HashData(fileBytes));
        return $"gemini-ocr:{_options.Model}:{mimeType}:{hash}";
    }

    private static LabelApplication CloneLabelApplication(LabelApplication application)
    {
        return new LabelApplication
        {
            ProductName = application.ProductName,
            BrandName = application.BrandName,
            AlcoholType = application.AlcoholType,
            AlcoholByVolume = application.AlcoholByVolume,
            NetContents = application.NetContents,
            ProducerOrImporter = application.ProducerOrImporter,
            CountryOfOrigin = application.CountryOfOrigin,
            GovernmentWarning = application.GovernmentWarning,
            ContainsSulfites = application.ContainsSulfites,
            RawText = application.RawText
        };
    }

    private static string BuildGeminiErrorMessage(HttpStatusCode statusCode, string? reasonPhrase, string responseBody)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            var retryDelay = ExtractRetryDelay(responseBody);
            var retryText = string.IsNullOrWhiteSpace(retryDelay)
                ? string.Empty
                : $" Google suggested retrying after about {retryDelay}.";

            return "Gemini quota/rate limit was reached for this API key or model."
                + retryText
                + " Wait for the quota to reset, enable billing/request a quota increase in Google AI Studio, or paste OCR text into the Optional OCR text box to keep testing without using Gemini.";
        }

        var providerMessage = ExtractProviderErrorMessage(responseBody);
        if (!string.IsNullOrWhiteSpace(providerMessage))
        {
            return $"Gemini OCR request failed: {(int)statusCode} {reasonPhrase}. {providerMessage}";
        }

        return $"Gemini OCR request failed: {(int)statusCode} {reasonPhrase}.";
    }

    private static string ExtractRetryDelay(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var details = document.RootElement.GetProperty("error").GetProperty("details");
            foreach (var detail in details.EnumerateArray())
            {
                if (detail.TryGetProperty("retryDelay", out var retryDelay))
                {
                    return retryDelay.GetString() ?? string.Empty;
                }
            }
        }
        catch (JsonException)
        {
        }
        catch (KeyNotFoundException)
        {
        }

        return string.Empty;
    }

    private static string ExtractProviderErrorMessage(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
        }
        catch (KeyNotFoundException)
        {
        }

        return string.Empty;
    }

    private static LabelApplication ParseGeminiLabelApplication(string geminiText)
    {
        var cleanedJson = CleanJson(geminiText);

        try
        {
            var extracted = JsonSerializer.Deserialize<GeminiLabelExtraction>(
                cleanedJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (extracted is not null)
            {
                return new LabelApplication
                {
                    ProductName = extracted.ProductName ?? string.Empty,
                    BrandName = extracted.BrandName ?? string.Empty,
                    AlcoholType = extracted.AlcoholType ?? string.Empty,
                    AlcoholByVolume = extracted.AlcoholByVolume ?? string.Empty,
                    NetContents = extracted.NetContents ?? string.Empty,
                    ProducerOrImporter = extracted.ProducerOrImporter ?? string.Empty,
                    CountryOfOrigin = extracted.CountryOfOrigin ?? string.Empty,
                    GovernmentWarning = extracted.GovernmentWarning ?? string.Empty,
                    ContainsSulfites = extracted.ContainsSulfites ?? string.Empty,
                    RawText = extracted.RawText ?? geminiText
                };
            }
        }
        catch (JsonException)
        {
            return ParseLabelText(geminiText);
        }

        return ParseLabelText(geminiText);
    }

    private static string ExtractGeminiText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);

        var candidates = document.RootElement.GetProperty("candidates");
        var parts = candidates[0].GetProperty("content").GetProperty("parts");

        var extractedText = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textElement))
            {
                extractedText.Append(textElement.GetString());
            }
        }

        return extractedText.ToString();
    }

    private static string CleanJson(string text)
    {
        var cleaned = text.Trim();
        if (!cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            return cleaned;
        }

        cleaned = Regex.Replace(cleaned, @"^```(?:json)?\s*", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s*```$", string.Empty);
        return cleaned.Trim();
    }

    private static bool IsTextUpload(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return extension is ".txt" or ".csv";
    }

    private static bool IsGeminiSupportedUpload(string mimeType)
    {
        return mimeType is "image/jpeg"
            or "image/png"
            or "image/webp"
            or "application/pdf";
    }

    private static string GetMimeType(IFormFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.ContentType) && file.ContentType != "application/octet-stream")
        {
            return file.ContentType;
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    private static Dictionary<string, object> CreateLabelResponseSchema()
    {
        var stringField = new Dictionary<string, object> { ["type"] = "string" };

        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["productName"] = stringField,
                ["brandName"] = stringField,
                ["alcoholType"] = stringField,
                ["alcoholByVolume"] = stringField,
                ["netContents"] = stringField,
                ["producerOrImporter"] = stringField,
                ["countryOfOrigin"] = stringField,
                ["governmentWarning"] = stringField,
                ["containsSulfites"] = stringField,
                ["rawText"] = stringField
            },
            ["required"] = new[]
            {
                "productName",
                "brandName",
                "alcoholType",
                "alcoholByVolume",
                "netContents",
                "producerOrImporter",
                "countryOfOrigin",
                "governmentWarning",
                "containsSulfites",
                "rawText"
            }
        };
    }

    private static LabelApplication ParseLabelText(string rawText)
    {
        return new LabelApplication
        {
            RawText = rawText,
            ProductName = FindValue(rawText, "product", "product name", "name"),
            BrandName = FindValue(rawText, "brand", "brand name"),
            AlcoholType = FindValue(rawText, "type", "class", "class/type", "category"),
            AlcoholByVolume = FindAbv(rawText),
            NetContents = FindNetContents(rawText),
            ProducerOrImporter = FindValue(rawText, "producer", "importer", "bottled by", "produced by", "manufacturer"),
            CountryOfOrigin = FindValue(rawText, "country", "origin", "country of origin"),
            GovernmentWarning = FindGovernmentWarning(rawText),
            ContainsSulfites = FindSulfites(rawText)
        };
    }

    private static string FindValue(string text, params string[] labels)
    {
        foreach (var label in labels)
        {
            var pattern = $@"(?im)^\s*{Regex.Escape(label)}\s*[:\-]\s*(.+?)\s*$";
            var match = Regex.Match(text, pattern);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return string.Empty;
    }

    private static string FindAbv(string text)
    {
        var match = Regex.Match(text, @"(?i)(\d{1,2}(?:\.\d{1,2})?)\s*%?\s*(?:alc\.?\s*/?\s*vol\.?|abv|alcohol by volume)");
        return match.Success ? $"{match.Groups[1].Value}% ABV" : FindValue(text, "abv", "alcohol by volume");
    }

    private static string FindNetContents(string text)
    {
        var match = Regex.Match(text, @"(?i)\b(\d+(?:\.\d+)?)\s*(ml|l|liter|liters|oz|fl oz|gallon|gal)\b");
        return match.Success ? match.Value.Trim() : FindValue(text, "net contents", "volume", "contents");
    }

    private static string FindGovernmentWarning(string text)
    {
        var warningMatch = Regex.Match(text, @"(?is)government warning[:\s]*(.+)");
        if (warningMatch.Success)
        {
            return warningMatch.Groups[1].Value.Trim();
        }

        return text.Contains("government warning", StringComparison.OrdinalIgnoreCase)
            ? "Government warning detected"
            : string.Empty;
    }

    private static string FindSulfites(string text)
    {
        return Regex.IsMatch(text, @"\bcontains?\s+sulfi(te|tes)\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(text, @"\bcontains?\s+sulphi(te|tes)\b", RegexOptions.IgnoreCase)
            ? "Yes"
            : string.Empty;
    }

    private sealed class GeminiLabelExtraction
    {
        public string? ProductName { get; set; }
        public string? BrandName { get; set; }
        public string? AlcoholType { get; set; }
        public string? AlcoholByVolume { get; set; }
        public string? NetContents { get; set; }
        public string? ProducerOrImporter { get; set; }
        public string? CountryOfOrigin { get; set; }
        public string? GovernmentWarning { get; set; }
        public string? ContainsSulfites { get; set; }
        public string? RawText { get; set; }
    }
}
