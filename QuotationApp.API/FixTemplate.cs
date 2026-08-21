using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

string templatePath = @"Templates\QuotationTemplate.docx";
string backupPath = @"Templates\QuotationTemplate_backup.docx";

File.Copy(templatePath, backupPath, true);

using (var doc = WordprocessingDocument.Open(templatePath, true))
{
    var body = doc.MainDocumentPart.Document.Body;
    var paragraphs = body.Elements<Paragraph>().ToList();

    bool foundFirst = false;
    bool foundSecond = false;
    int firstIndex = -1;
    int secondIndex = -1;

    // Find both "Terms and Conditions" headings
    for (int i = 0; i < paragraphs.Count; i++)
    {
        var text = paragraphs[i].InnerText.Trim();
        if (text.Equals("Terms and Conditions", StringComparison.OrdinalIgnoreCase))
        {
            if (!foundFirst)
            {
                foundFirst = true;
                firstIndex = i;
            }
            else if (!foundSecond)
            {
                foundSecond = true;
                secondIndex = i;
                break;
            }
        }
    }

    if (foundFirst && foundSecond && firstIndex >= 0 && secondIndex > firstIndex)
    {
        // Remove all paragraphs from firstIndex to just before secondIndex
        for (int i = secondIndex - 1; i >= firstIndex; i--)
        {
            paragraphs[i].Remove();
        }
        Console.WriteLine($"Removed first Terms section (paragraphs {firstIndex} to {secondIndex - 1})");
    }
    else
    {
        Console.WriteLine("Could not find both Terms sections");
    }

    doc.MainDocumentPart.Document.Save();
}

Console.WriteLine("Template fixed successfully");