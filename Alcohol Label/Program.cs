using Alcohol_Label.Services;

var builder = WebApplication.CreateBuilder(args);

var hostingPort = Environment.GetEnvironmentVariable("PORT")
    ?? Environment.GetEnvironmentVariable("WEBSITES_PORT");

if (!string.IsNullOrWhiteSpace(hostingPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{hostingPort}");
}

builder.Services.AddRazorPages();
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.AddHttpClient<IOcrService, OcrService>();
builder.Services.AddScoped<ILabelVerificationService, LabelVerificationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
