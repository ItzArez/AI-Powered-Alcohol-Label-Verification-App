using System.Globalization;
using System.Text.RegularExpressions;
using Alcohol_Label.Models;

namespace Alcohol_Label.Services;

public class LabelVerificationService : ILabelVerificationService
{
    public VerificationResult Verify(LabelApplication application, ExpectedLabelValues? expectedValues = null)
    {
        expectedValues ??= new ExpectedLabelValues();

        var checks = new List<FieldCheckResult>
        {
            Required("Brand Name", application.BrandName),
            Required("Class/Type", GetClassTypeValue(application)),
            ValidateAbv(application.AlcoholByVolume),
            Required("Net Contents", application.NetContents),
            Required("Producer/Bottler", application.ProducerOrImporter),
            ValidateGovernmentWarning(application.GovernmentWarning, application.RawText)
        };

        if (expectedValues.RequireCountryOfOrigin || HasValue(expectedValues.CountryOfOrigin))
        {
            checks.Add(Required("Country of Origin", application.CountryOfOrigin, expectedValues.CountryOfOrigin));
        }

        AddExpectedComparisonChecks(checks, application, expectedValues);
        AddSulfitesCheck(checks, application, expectedValues);

        return new VerificationResult
        {
            Application = application,
            ExpectedValues = expectedValues,
            Checks = checks
        };
    }

    private static void AddExpectedComparisonChecks(
        List<FieldCheckResult> checks,
        LabelApplication application,
        ExpectedLabelValues expectedValues)
    {
        AddTextComparison(checks, "Expected Brand Name", expectedValues.BrandName, application.BrandName);
        AddClassTypeComparison(checks, expectedValues.ClassType, application);
        AddAbvComparison(checks, expectedValues.AlcoholContent, application.AlcoholByVolume);
        AddNetContentsComparison(checks, expectedValues.NetContents, application.NetContents);
        AddTextComparison(checks, "Expected Producer/Bottler", expectedValues.ProducerBottler, application.ProducerOrImporter);
        AddTextComparison(checks, "Expected Country of Origin", expectedValues.CountryOfOrigin, application.CountryOfOrigin);
        AddTextComparison(checks, "Expected Government Warning", expectedValues.GovernmentWarning, $"{application.GovernmentWarning} {application.RawText}");
    }

    private static void AddTextComparison(List<FieldCheckResult> checks, string fieldName, string expected, string extracted)
    {
        if (!HasValue(expected))
        {
            return;
        }

        var passed = TextMatches(expected, extracted);
        checks.Add(new FieldCheckResult
        {
            FieldName = fieldName,
            ExpectedValue = expected,
            ExtractedValue = extracted,
            Passed = passed,
            Message = passed ? "Matches expected value." : "Does not match expected value."
        });
    }

    private static void AddClassTypeComparison(
        List<FieldCheckResult> checks,
        string expectedClassType,
        LabelApplication application)
    {
        if (!HasValue(expectedClassType))
        {
            return;
        }

        var extractedCandidates = new[]
        {
            application.AlcoholType,
            application.ProductName,
            GetClassTypeValue(application)
        };

        var passed = extractedCandidates.Any(candidate => TextMatches(expectedClassType, candidate));
        checks.Add(new FieldCheckResult
        {
            FieldName = "Expected Class/Type",
            ExpectedValue = expectedClassType,
            ExtractedValue = GetClassTypeValue(application),
            Passed = passed,
            Message = passed ? "Matches expected class/type." : "Class/type does not match expected value."
        });
    }

    private static void AddAbvComparison(List<FieldCheckResult> checks, string expectedAbv, string extractedAbv)
    {
        if (!HasValue(expectedAbv))
        {
            return;
        }

        var expected = ExtractNumber(expectedAbv);
        var extracted = ExtractNumber(extractedAbv);
        var passed = expected.HasValue
            && extracted.HasValue
            && Math.Abs(expected.Value - extracted.Value) <= 0.05m;

        checks.Add(new FieldCheckResult
        {
            FieldName = "Expected Alcohol Content",
            ExpectedValue = expectedAbv,
            ExtractedValue = extractedAbv,
            Passed = passed,
            Message = passed ? "ABV matches expected value." : "ABV does not match expected value."
        });
    }

    private static void AddNetContentsComparison(List<FieldCheckResult> checks, string expectedNetContents, string extractedNetContents)
    {
        if (!HasValue(expectedNetContents))
        {
            return;
        }

        var expectedNumber = ExtractNumber(expectedNetContents);
        var extractedNumber = ExtractNumber(extractedNetContents);
        var expectedUnit = ExtractUnit(expectedNetContents);
        var extractedUnit = ExtractUnit(extractedNetContents);
        var passed = expectedNumber.HasValue
            && extractedNumber.HasValue
            && Math.Abs(expectedNumber.Value - extractedNumber.Value) <= 0.05m
            && expectedUnit == extractedUnit;

        checks.Add(new FieldCheckResult
        {
            FieldName = "Expected Net Contents",
            ExpectedValue = expectedNetContents,
            ExtractedValue = extractedNetContents,
            Passed = passed,
            Message = passed ? "Net contents match expected value." : "Net contents do not match expected value."
        });
    }

    private static void AddSulfitesCheck(
        List<FieldCheckResult> checks,
        LabelApplication application,
        ExpectedLabelValues expectedValues)
    {
        if (string.Equals(expectedValues.SulfitesRequirement, "Ignore", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var detected = DetectSulfites(application);
        var required = string.Equals(expectedValues.SulfitesRequirement, "Required", StringComparison.OrdinalIgnoreCase);
        var passed = required ? detected : !detected;

        checks.Add(new FieldCheckResult
        {
            FieldName = "Contains Sulfites",
            ExpectedValue = required ? "Required" : "Not expected",
            ExtractedValue = detected ? "Detected" : "Not detected",
            Passed = passed,
            Message = passed
                ? "Sulfites check matched expectation."
                : "Sulfites check did not match expectation."
        });
    }

    private static FieldCheckResult Required(string fieldName, string value, string expectedValue = "")
    {
        var passed = !string.IsNullOrWhiteSpace(value);
        return new FieldCheckResult
        {
            FieldName = fieldName,
            ExpectedValue = expectedValue,
            ExtractedValue = value,
            Passed = passed,
            Message = passed ? "Present" : $"{fieldName} was not found."
        };
    }

    private static FieldCheckResult ValidateAbv(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new FieldCheckResult
            {
                FieldName = "Alcohol Content",
                ExtractedValue = value,
                Passed = false,
                Message = "Alcohol content was not found."
            };
        }

        var abv = ExtractNumber(value);
        var passed = abv.HasValue && abv.Value > 0 && abv.Value <= 95;

        return new FieldCheckResult
        {
            FieldName = "Alcohol Content",
            ExtractedValue = value,
            Passed = passed,
            Message = passed ? "ABV appears valid." : "ABV must be a realistic percentage between 0 and 95."
        };
    }

    private static FieldCheckResult ValidateGovernmentWarning(string warning, string rawText)
    {
        var combinedText = $"{warning} {rawText}";
        var hasGovernmentWarning = combinedText.Contains("government warning", StringComparison.OrdinalIgnoreCase);
        var hasPregnancyWarning = Regex.IsMatch(combinedText, @"\bpregnan(t|cy)\b", RegexOptions.IgnoreCase)
            || combinedText.Contains("birth defects", StringComparison.OrdinalIgnoreCase);
        var hasDrivingWarning = combinedText.Contains("drive", StringComparison.OrdinalIgnoreCase)
            || combinedText.Contains("machinery", StringComparison.OrdinalIgnoreCase)
            || combinedText.Contains("operate machinery", StringComparison.OrdinalIgnoreCase);

        var passed = hasGovernmentWarning && hasPregnancyWarning && hasDrivingWarning;

        return new FieldCheckResult
        {
            FieldName = "Government Warning",
            ExtractedValue = warning,
            Passed = passed,
            Message = passed
                ? "Required warning language appears to be present."
                : "Government warning appears incomplete or missing."
        };
    }

    private static string GetClassTypeValue(LabelApplication application)
    {
        var values = new[] { application.AlcoholType, application.ProductName }
            .Where(HasValue)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(" / ", values);
    }

    private static bool DetectSulfites(LabelApplication application)
    {
        var combinedText = $"{application.ContainsSulfites} {application.RawText}";
        return Regex.IsMatch(combinedText, @"\bcontains?\s+sulfi(te|tes)\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(combinedText, @"\bcontains?\s+sulphi(te|tes)\b", RegexOptions.IgnoreCase);
    }

    private static bool TextMatches(string expected, string extracted)
    {
        if (!HasValue(expected) || !HasValue(extracted))
        {
            return false;
        }

        var normalizedExpected = NormalizeText(expected);
        var normalizedExtracted = NormalizeText(extracted);

        return normalizedExpected == normalizedExtracted
            || normalizedExtracted.Contains(normalizedExpected)
            || normalizedExpected.Contains(normalizedExtracted);
    }

    private static string NormalizeText(string value)
    {
        var normalized = value.ToUpperInvariant();
        normalized = Regex.Replace(normalized, @"[^A-Z0-9]+", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static decimal? ExtractNumber(string value)
    {
        var match = Regex.Match(value, @"(\d{1,4}(?:\.\d{1,2})?)");
        if (!match.Success)
        {
            return null;
        }

        return decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string ExtractUnit(string value)
    {
        var normalized = NormalizeText(value);
        if (Regex.IsMatch(normalized, @"\bML\b|MILLILITER|MILLILITRE"))
        {
            return "ML";
        }

        if (Regex.IsMatch(normalized, @"\bL\b|LITER|LITRE"))
        {
            return "L";
        }

        if (Regex.IsMatch(normalized, @"OZ|OUNCE"))
        {
            return "OZ";
        }

        return string.Empty;
    }

    private static bool HasValue(string value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }
}
