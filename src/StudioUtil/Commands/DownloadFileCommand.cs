using Community.VisualStudio.Toolkit;
using Community.VisualStudio.Toolkit.DependencyInjection;
using Community.VisualStudio.Toolkit.DependencyInjection.Core;
using EnvDTE80;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using StudioUtil.Dtos;
using StudioUtil.Utils;
using StudioUtil.Utils.Abstract;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace StudioUtil.Commands;

#nullable enable

[Command(PackageIds.DownloadFileCommand)]
public class DownloadFileCommand : BaseDICommand
{
    private readonly IFileUtilSync _fileUtil;
    private readonly IDteUtil _dteUtil;
    private readonly INewItemFactory _newItemFactory;
    private readonly ILogger<DownloadFileCommand> _logger;
    private readonly ISolutionUtil _solutionUtil;

    public DownloadFileCommand(DIToolkitPackage package, IFileUtilSync fileUtil, IDteUtil dteUtil, INewItemFactory newItemFactory,
        ILogger<DownloadFileCommand> logger, ISolutionUtil solutionUtil) : base(package)
    {
        _fileUtil = fileUtil;
        _dteUtil = dteUtil;
        _newItemFactory = newItemFactory;
        _logger = logger;
        _solutionUtil = solutionUtil;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        _logger.LogInformation("Starting clipboard processing for class creation.");

        var clipboardText = Clipboard.GetText();

        // Split clipboard text into class definitions
        var uri = GetUriIfClipboardAlreadyContains(clipboardText);

        if (string.IsNullOrEmpty(uri))
        {
            _logger.LogDebug("No Uri found in clipboard content.");
        }

        var result = PromptForUri(uri);

        ValidateInput(result);

        var directory = await GetTargetDirectory().ConfigureAwait(false);

        string? filePath = null;

        try
        {
            filePath = await DownloadFile(result.Target, directory).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file.");
            MessageBox.Show($"Failed to download file:\n{ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        if (filePath != null)
            await AddFile(filePath).ConfigureAwait(false);

        await _solutionUtil.RefreshSolutionExplorer();
        _logger.LogInformation("Finished downloading file.");
    }

    public async ValueTask<string> GetTargetDirectory()
    {
        try
        {
            var dte = await _dteUtil.GetDte();
            if (dte == null)
            {
                _logger.LogError("Failed to retrieve DTE instance.");
                throw new InvalidOperationException("DTE instance is null.");
            }

            var target = await _newItemFactory.Create(dte);
            if (target == null)
            {
                _logger.LogError("Failed to retrieve target project or folder.");
                throw new InvalidOperationException("Target project or folder is null.");
            }

            string? filePath = target.ProjectItem?.GetFileName();

            // Handle the case where the project is a directory-based selection
            if (string.IsNullOrEmpty(filePath))
            {
                _logger.LogWarning("Project item does not have a file path. Trying alternative methods.");

                var project = target.ProjectItem?.ContainingProject;
                if (project != null && !string.IsNullOrEmpty(project.FullName))
                {
                    filePath = project.FullName;
                }
            }

            // Extract the directory
            var directory = !string.IsNullOrEmpty(filePath) ? Path.GetDirectoryName(filePath) : null;

            if (string.IsNullOrEmpty(directory))
            {
                directory = target.Directory;
            }

            if (string.IsNullOrEmpty(directory))
            {
                _logger.LogError("Failed to determine target directory from project.");
                throw new DirectoryNotFoundException("Could not determine target directory.");
            }

            _logger.LogInformation($"Target directory determined: {directory}");

            return directory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while determining the target directory.");
            throw;
        }
    }

    private static string GetUriIfClipboardAlreadyContains(string clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
            return "";

        // Regex to match URLs (supports http, https, ftp, and file schemes)
        const string urlPattern = @"\b(?:https?|ftp|file)://[^\s""']+";

        var match = Regex.Match(clipboardText, urlPattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Value : "";
    }

    private async ValueTask<string> DownloadFile(string fileUrl, string directory)
    {
        using var httpClient = new HttpClient();

        // Send a HEAD request first to check the headers
        var headRequest = new HttpRequestMessage(HttpMethod.Head, fileUrl);
        var headResponse = await httpClient.SendAsync(headRequest);

        string? fileName = null;

        if (headResponse.IsSuccessStatusCode && headResponse.Content.Headers.ContentDisposition != null)
        {
            // Try to extract filename from the Content-Disposition header
            fileName = headResponse.Content.Headers.ContentDisposition.FileName?.Trim('"');
        }

        if (string.IsNullOrEmpty(fileName))
        {
            // Extract filename from URL if not found in headers
            fileName = Path.GetFileName(new Uri(fileUrl).LocalPath);
        }

        // If filename is still empty, assign a default name
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = "downloaded_file_" + DateTime.UtcNow.Ticks;
        }

        var filePath = Path.Combine(directory, fileName);

        _logger.LogInformation($"Downloading file from {fileUrl} to {filePath}");

        var response = await httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var httpStream = await response.Content.ReadAsStreamAsync();
        await httpStream.CopyToAsync(fileStream);

        _logger.LogInformation($"File download complete: {filePath}");
        return filePath;
    }

    private static void ValidateInput(DownloadFileDto downloadFileDto)
    {
        if (!downloadFileDto.Result)
            throw new Exception("Messagebox was early exited");

        if (string.IsNullOrEmpty(downloadFileDto.Target))
            throw new ArgumentNullException(nameof(downloadFileDto.Target));
    }

    private static DownloadFileDto PromptForUri(string? target)
    {
        var dialog = new DownloadFileDialog
        {
            Owner = Application.Current.MainWindow,
            Target = target
        };

        var result = dialog.ShowDialog();

        var dto = new DownloadFileDto
        {
            Result = result.GetValueOrDefault(),
            Target = dialog.TargetInput
        };

        return dto;
    }

    private async ValueTask AddFile(string filePath)
    {
        try
        {
            var dte = await _dteUtil.GetDte();

            _logger.LogInformation("Adding file ({filePath})", filePath);

            dte?.Solution.AddFromFile(filePath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error adding file");
        }
    }
}