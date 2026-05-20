# AI-Powered Alcohol Label Verification App

Prototype ASP.NET Core Razor Pages application that uses the Gemini API to extract text and structured fields from alcohol label images, then checks the extracted label data against required fields and optional expected values.

## GitHub

Repository: https://github.com/ItzArez/AI-Powered-Alcohol-Label-Verification-App

## Features

- Upload alcohol label images or PDFs for AI-assisted OCR.
- Paste OCR text manually for quick testing.
- Extract brand name, class/type, alcohol content, net contents, producer/bottler, country of origin, government warning, and sulfites.
- Compare extracted fields against optional expected values.
- Flag mismatches, missing fields, invalid ABV, missing government warning language, and sulfites expectation failures.
- Optimize large image uploads in the browser before sending them to Gemini.
- Display a clear pass/review summary with field-by-field results.

## Tech Stack

- .NET 8
- ASP.NET Core Razor Pages
- Google Gemini API
- `gemini-2.5-flash-lite` for faster multimodal extraction
- HTML, CSS, and JavaScript
- .NET user-secrets for local API key storage

No extra NuGet packages are required beyond the ASP.NET Core web SDK.

## Project Structure

```text
Alcohol Label/
  Models/
    ExpectedLabelValues.cs
    FieldCheckResult.cs
    LabelApplication.cs
    VerificationResult.cs
  Pages/
    Index.cshtml
    Results.cshtml
    Documentation.cshtml
  Services/
    GeminiOptions.cs
    IOcrService.cs
    OcrService.cs
    ILabelVerificationService.cs
    LabelVerificationService.cs
  wwwroot/
    css/site.css
    js/site.js
  Program.cs
  appsettings.json
```

## Setup

1. Install the .NET 8 SDK.
2. Clone the repository:

```powershell
git clone https://github.com/ItzArez/AI-Powered-Alcohol-Label-Verification-App.git
cd AI-Powered-Alcohol-Label-Verification-App
```

3. Set your Gemini API key with user-secrets:

```powershell
cd "Alcohol Label"
dotnet user-secrets set "Gemini:ApiKey" "YOUR_GEMINI_API_KEY"
```

4. Restore and run:

```powershell
dotnet restore
dotnet run
```

5. Open the local URL shown in the terminal, usually:

```text
https://localhost:7245
http://localhost:5035
```

## Visual Studio Run Instructions

1. Open `Alcohol Label.sln` in Visual Studio.
2. Make sure `Alcohol Label` is the startup project.
3. Set the Gemini API key with user-secrets from the terminal:

```powershell
cd "C:\Users\aashi\source\repos\Alcohol Label\Alcohol Label"
dotnet user-secrets set "Gemini:ApiKey" "YOUR_GEMINI_API_KEY"
```

4. Press `F5` or click Run.

## Railway Deployment

This repository includes a root `Dockerfile` for Railway. Railway detects the Dockerfile, builds the ASP.NET Core app, and runs it on Railway's `PORT` environment variable.

### Deploy from GitHub

1. Go to Railway and create a new project.
2. Choose **Deploy from GitHub repo**.
3. Select:

```text
ItzArez/AI-Powered-Alcohol-Label-Verification-App
```

4. Add this Railway service variable:

```text
Gemini__ApiKey=YOUR_GEMINI_API_KEY
```

5. Optional production variable:

```text
ASPNETCORE_ENVIRONMENT=Production
```

6. Deploy the service and generate a public domain from Railway's networking settings.

The double underscore in `Gemini__ApiKey` is intentional. ASP.NET Core maps it to `Gemini:ApiKey`.

## Testing Plan

### Test 1: Perfect Pass

Use the TTB wine sample label with these expected values:

```text
Brand Name: ABC WINERY
Class/Type: AMERICAN MERLOT
Alcohol Content: ALC. 15.5% BY VOL.
Net Contents: 750 ML
Producer/Bottler: XYZ VINTNERS
Country of Origin: leave blank
Sulfites: Required
Require country of origin: unchecked
```

Expected result: `Label Passed`.

### Test 2: Mismatch

Use the same TTB wine label but enter the wrong alcohol content:

```text
Alcohol Content: 14%
```

Expected result: `Needs Review` with an ABV mismatch.

### Test 3: Missing Field

Use pasted text or an image where producer/bottler is missing. To test country of origin, check:

```text
Require country of origin
```

Expected result: `Needs Review` with the missing field listed.

### Test 4: Real-World OCR

Use a real bottle back-label image where ABV, net contents, and government warning are visible.

Useful search terms:

```text
wine back label government warning 750 ml alc by vol
bourbon back label government warning 750 ml 45% alc vol
vodka label government warning 40% alc vol 750 ml
beer label government warning alc vol
```

Expected result: depends on image quality and visible fields.

## Approach

The app separates the workflow into three layers:

- Razor Pages handle file upload, expected-value input, and results display.
- `OcrService` sends uploaded images/PDFs to Gemini using structured JSON output and maps the response into a `LabelApplication`.
- `LabelVerificationService` checks required fields and compares extracted values against optional expected values.

For speed, large image uploads are resized in the browser before submission. Gemini is configured for structured JSON output, low temperature, and `gemini-2.5-flash-lite`.

## Assumptions

- The app is a prototype for a take-home project, not a production regulatory system.
- Expected values are entered manually by the reviewer.
- Country of origin is optional unless the reviewer marks it as required.
- Government warning validation checks for key required phrases, not an exact legal text match unless an expected warning is provided.
- Sulfites are checked when the reviewer selects `Required` or `Not expected`.

## Limitations

- The app does not query TTB, COLA, permit, or formula databases.
- It does not persist uploaded files or verification history.
- OCR quality depends on the uploaded image quality.
- Curved, blurry, low-resolution, or glare-heavy labels may need human review.
- The app does not provide legal advice or final compliance approval.

## API Key Security

Do not commit a real Gemini API key to GitHub. `appsettings.json` intentionally keeps the key blank:

```json
{
  "Gemini": {
    "ApiKey": ""
  }
}
```

Use `.NET user-secrets` locally or environment variables in deployment.
