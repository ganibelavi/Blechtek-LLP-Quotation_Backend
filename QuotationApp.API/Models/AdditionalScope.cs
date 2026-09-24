namespace QuotationApp.API.Models;

public class AdditionalScope
{
    public int Id { get; set; }
    public string QuotationId { get; set; } = string.Empty;
    public string Requirement { get; set; } = string.Empty;
    public int? ModulesId { get; set; }
    public string Modules { get; set; } = string.Empty;
    public int NoOfManpower { get; set; }
    public int NoOfDays { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
    public decimal Price { get; set; }
}