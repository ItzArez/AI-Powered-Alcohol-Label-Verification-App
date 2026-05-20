namespace Alcohol_Label.Models;

public class ExpectedLabelValues
{
    public string BrandName { get; set; } = string.Empty;
    public string ClassType { get; set; } = string.Empty;
    public string AlcoholContent { get; set; } = string.Empty;
    public string NetContents { get; set; } = string.Empty;
    public string ProducerBottler { get; set; } = string.Empty;
    public string CountryOfOrigin { get; set; } = string.Empty;
    public string GovernmentWarning { get; set; } = string.Empty;
    public bool RequireCountryOfOrigin { get; set; }
    public string SulfitesRequirement { get; set; } = "Ignore";
}
