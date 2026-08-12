namespace QuotationApp.API.Services;

public interface IPdfConverterService
{
    /// <summary>Converts the given .docx to .pdf in the same folder and returns the .pdf path.</summary>
    Task<string> ConvertToPdfAsync(string docxPath);
}
