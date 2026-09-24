using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace QuotationApp.API.Services;

public class PdfConverterService : IPdfConverterService
{
    private readonly string _contentRoot;

    public PdfConverterService(
        IOptions<QuotationSettings> settings,
        IWebHostEnvironment env)
    {
        _contentRoot = env.ContentRootPath;
    }

    public async Task<string> ConvertToPdfAsync(string docxPath)
    {
        if (string.IsNullOrWhiteSpace(docxPath))
        {
            throw new ArgumentException(
                "DOCX path cannot be null or empty.",
                nameof(docxPath));
        }

        if (!File.Exists(docxPath))
        {
            throw new FileNotFoundException(
                "The Word document was not found.",
                docxPath);
        }

        // Convert to absolute path so that LibreOffice receives
        // a reliable file path.
        docxPath = Path.GetFullPath(docxPath);

        var outputFolder = Path.GetDirectoryName(docxPath);

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            throw new InvalidOperationException(
                "Unable to determine the output folder for the Word document.");
        }

        Directory.CreateDirectory(outputFolder);

        var expectedPdfPath = Path.Combine(
            outputFolder,
            Path.GetFileNameWithoutExtension(docxPath) + ".pdf");

        // Delete an existing PDF so that an old PDF is never
        // accidentally returned.
        if (File.Exists(expectedPdfPath))
        {
            try
            {
                File.Delete(expectedPdfPath);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    $"Unable to delete the existing PDF file: {expectedPdfPath}",
                    ex);
            }
        }

        await ConvertDocxToPdfUsingLibreOfficeAsync(
            docxPath,
            outputFolder);

        // Verify that the PDF was actually generated.
        if (!File.Exists(expectedPdfPath))
        {
            throw new InvalidOperationException(
                "The Word document conversion completed, but the expected " +
                $"PDF file was not found.\n\nExpected PDF:\n{expectedPdfPath}");
        }

        return expectedPdfPath;
    }

    private async Task ConvertDocxToPdfUsingLibreOfficeAsync(
        string docxPath,
        string outputFolder)
    {
        var libreOfficePath = FindLibreOfficeExecutable();

        if (string.IsNullOrWhiteSpace(libreOfficePath))
        {
            throw new InvalidOperationException(
                "LibreOffice was not found on this machine/server.\n\n" +
                "Please install LibreOffice and make sure soffice.exe " +
                "is available.\n\n" +
                "Windows default location:\n" +
                @"C:\Program Files\LibreOffice\program\soffice.exe" +
                "\n\n" +
                "Linux default location:\n" +
                "/usr/bin/soffice");
        }

        var arguments =
            $"--headless " +
            $"--convert-to pdf " +
            $"--outdir \"{outputFolder}\" " +
            $"\"{docxPath}\"";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = libreOfficePath,
            Arguments = arguments,

            UseShellExecute = false,
            CreateNoWindow = true,

            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process
        {
            StartInfo = processStartInfo
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "Unable to start LibreOffice.");
            }

            // Read both streams asynchronously to avoid deadlocks.
            var standardOutputTask =
                process.StandardOutput.ReadToEndAsync();

            var standardErrorTask =
                process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var standardOutput =
                await standardOutputTask;

            var standardError =
                await standardErrorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "LibreOffice failed to convert the Word document to PDF.\n\n" +
                    $"Exit Code: {process.ExitCode}\n\n" +
                    $"LibreOffice Output:\n{standardOutput}\n\n" +
                    $"LibreOffice Error:\n{standardError}");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "An error occurred while converting the Word document to PDF.",
                ex);
        }
    }

    private string FindLibreOfficeExecutable()
    {
        // ============================================================
        // 1. Check LIBREOFFICE_PATH environment variable
        // ============================================================

        var environmentPath =
            Environment.GetEnvironmentVariable("LIBREOFFICE_PATH");

        if (!string.IsNullOrWhiteSpace(environmentPath) &&
            File.Exists(environmentPath))
        {
            return environmentPath;
        }

        // ============================================================
        // 2. Windows
        // ============================================================

        if (OperatingSystem.IsWindows())
        {
            var programFiles =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles);

            var programFilesX86 =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86);

            var windowsCandidates = new[]
            {
                // Standard 64-bit installation
                Path.Combine(
                    programFiles,
                    "LibreOffice",
                    "program",
                    "soffice.exe"),

                // Standard 32-bit installation
                Path.Combine(
                    programFilesX86,
                    "LibreOffice",
                    "program",
                    "soffice.exe"),

                // Explicit fallback
                @"C:\Program Files\LibreOffice\program\soffice.exe",

                // Explicit fallback
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
            };

            var windowsPath = windowsCandidates
                .FirstOrDefault(File.Exists);

            if (!string.IsNullOrWhiteSpace(windowsPath))
            {
                return windowsPath;
            }
        }

        // ============================================================
        // 3. Linux
        // ============================================================

        if (OperatingSystem.IsLinux())
        {
            var linuxCandidates = new[]
            {
                "/usr/bin/soffice",
                "/usr/local/bin/soffice",
                "/usr/bin/libreoffice",
                "/usr/local/bin/libreoffice"
            };

            var linuxPath = linuxCandidates
                .FirstOrDefault(File.Exists);

            if (!string.IsNullOrWhiteSpace(linuxPath))
            {
                return linuxPath;
            }
        }

        // ============================================================
        // 4. Search PATH
        // ============================================================

        var executableName =
            OperatingSystem.IsWindows()
                ? "soffice.exe"
                : "soffice";

        try
        {
            var pathEnvironment =
                Environment.GetEnvironmentVariable("PATH");

            if (!string.IsNullOrWhiteSpace(pathEnvironment))
            {
                var pathEntries =
                    pathEnvironment.Split(
                        Path.PathSeparator,
                        StringSplitOptions.RemoveEmptyEntries);

                foreach (var pathEntry in pathEntries)
                {
                    try
                    {
                        var possiblePath =
                            Path.Combine(
                                pathEntry,
                                executableName);

                        if (File.Exists(possiblePath))
                        {
                            return possiblePath;
                        }
                    }
                    catch
                    {
                        // Ignore invalid PATH entries
                    }
                }
            }
        }
        catch
        {
            // Ignore PATH lookup errors
        }

        return string.Empty;
    }
}