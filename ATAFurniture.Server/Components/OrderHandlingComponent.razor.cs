using ATAFurniture.Server.DataAccess;
using ATAFurniture.Server.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Domain.TemplateBuilding;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using sib_api_v3_sdk.Api;
using sib_api_v3_sdk.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace ATAFurniture.Server.Components;

public partial class OrderHandlingComponent
{
    [Inject] private FileGeneratorService FileGenerator { get; set; }
    [Inject] private IServiceProvider ServiceProvider { get; set; }
    [Inject] private ILogger<OrderHandlingComponent> Logger { get; set; }
    [Inject] private IConfiguration Configuration { get; set; }
    [Parameter] public UserContextService UserContextService { get; set; }
    [Parameter] public ConverterContext Context { get; set; }
    private bool _shouldGenerateFiles = false;
    private bool _shouldSendEmail = false;
    private bool _areFilesGenerating = false;
    private bool _sendingEmail = false;
    private bool _isTestEmail = true;
    private bool _isCreditConsumed = false;
    private SendSmtpEmailTo _toEmail = null;
    private readonly List<DirectoryInfo> _sessionDirs = [];
    private List<FileDisplayContext> _files = [];
    private bool? _isEmailSentSuccessful = null;
    protected override void OnInitialized()
    {
        Context.PropertyChanged += ContextOnPropertyChanged;
    }
    private void ContextOnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConverterContext.Files))
        {
            _shouldSendEmail = false;
            _shouldGenerateFiles = false;
            _isCreditConsumed = false;
            _files.Clear();
        }
        StateHasChanged();
    }
    private async Task OnCreateFilesOptionSelected()
    {
        _shouldGenerateFiles = true;
        _shouldSendEmail = false;
        var alreadyGeneratedFiles = await GenerateFiles();
        _files = await SaveFilesForDownload(alreadyGeneratedFiles);
        _areFilesGenerating = false;
        StateHasChanged();
    }
    private async Task<List<FileSaveContext>> GenerateFiles()
    {
        var fileNameProvider = ServiceProvider.GetKeyedService<IFileNameProvider>(Context.TargetCompany.Name);
        if (fileNameProvider is null)
        {
            Logger.LogWarning("No file name provider found for company {CompanyName}", Context.TargetCompany.Name);
        }

        var templateBuilder = ServiceProvider.GetKeyedService<ITemplateBuilder>(Context.TargetCompany.Name) as ITemplateBuilder;
        if (templateBuilder is null)
        {
            Logger.LogWarning("No template builder found for company {CompanyName}", Context.TargetCompany.Name);
        }

        _areFilesGenerating = true;
        var alreadyGeneratedFiles = await FileGenerator.CreateFiles(
            Context.ContactInfo,
            Context.Files,
            templateBuilder,
            fileNameProvider,
            Context.DifferentEdgeColor,
            Context.TargetCompany.Name == SupportedCompanies.MegaTrading.Name);
        return alreadyGeneratedFiles;
    }
    private async Task<List<FileDisplayContext>> SaveFilesForDownload(List<FileSaveContext> alreadyGeneratedFiles)
    {
        var result = new List<FileDisplayContext>();
        var storageConnectionString = Configuration["AzureStorageConnectionString"];
        var storageContainerName = Configuration["StorageContainerName"];
        
        var blobContainer = new BlobContainerClient(storageConnectionString, storageContainerName);
        // Ensure the container exists (no-op when it already does, e.g. in production). The download
        // links are bare blob URIs, so the container must allow public blob read.
        await blobContainer.CreateIfNotExistsAsync(PublicAccessType.Blob);
        foreach (var file in alreadyGeneratedFiles)
        {
            result.Add(await CreateFileDownloadLink(blobContainer, file));
        }

        return result;
    }
    private async Task<FileDisplayContext> CreateFileDownloadLink(BlobContainerClient container, FileSaveContext context)
    {
        var filesRootPath = "files";
        var salt = Guid.NewGuid().ToString();
        var sessionFolder = Path.Combine(
            Environment.CurrentDirectory,
            filesRootPath,
            salt);
        var sessionDir = Directory.CreateDirectory(sessionFolder);
        _sessionDirs.Add(sessionDir);

        await File.WriteAllBytesAsync(
            Path.Combine(
                sessionDir.FullName,
                context.FileName),
            context.Content);

        await container.DeleteBlobIfExistsAsync(context.FileName);
        var response = await container.UploadBlobAsync(context.FileName, BinaryData.FromBytes(context.Content));
        
        var blobClient = container.GetBlobClient(context.FileName);
        return new()
        {
            Name = context.FileName,
            Url = $"{blobClient.Uri}"
        };
    }
    private void OnSendEmailOptionSelected()
    {
        _shouldGenerateFiles = false;
        _shouldSendEmail = true;

        StateHasChanged();
    }
    private async Task OnSendEmailClicked()
    {
        _sendingEmail = true;

        // TODO extract in dedicated service
        await SendEmailUsingSendInBlue();

        if (_isEmailSentSuccessful.HasValue && _isEmailSentSuccessful.Value)
        {
            await UserContextService.ConsumeSingleCredit();
        }
        _sendingEmail = false;
    }
    private async Task SendEmailUsingSendInBlue()
    {
        var settings = new EmailSettings();
        Configuration.GetSection("EmailSettings").Bind(settings);
        var client = new TransactionalEmailsApi();
        client.Configuration.ApiKey["api-key"] = settings.ApiKey;
        var files = await GenerateFiles();
        var usedMaterials = Context.Details.Select(d => d.Material).Distinct().ToList();
        _toEmail = _isTestEmail ?
            new SendSmtpEmailTo(Context.ContactInfo.Email) :
            new SendSmtpEmailTo(Context.TargetCompany.Email, Context.TargetCompany.Name);
        var email = new SendSmtpEmail(
            new SendSmtpEmailSender(settings.Name, settings.Email),
            [_toEmail],
            templateId: settings.EmailTemplateId,
            _params: new Dictionary<string, object>
            {
                {
                    "CompanyName", Context.ContactInfo.CompanyName
                },
                {
                    "CompanyMobile", Context.ContactInfo.MobileNumber
                },
                {
                    "CompanyEmail", Context.ContactInfo.Email
                },
                {
                    "MaterialList", usedMaterials
                }
            },
            attachment: files.Select(f => new SendSmtpEmailAttachment
            {
                Name = f.FileName,
                Content = f.Content
            }).ToList()
        );

        try
        {
            await client.SendTransacEmailAsync(email);
            _isEmailSentSuccessful = true;
        }
        catch (Exception e)
        {
            _isEmailSentSuccessful = false;
        }
    }
    private void OnFileLinkClicked()
    {
        if (!_isCreditConsumed)
        {
            _isCreditConsumed = true;
            UserContextService.ConsumeSingleCredit().ConfigureAwait(false);
        }
    }
    public void Dispose()
    {
        Context.PropertyChanged -= ContextOnPropertyChanged;
        _files.Clear();

        foreach (var sessionDir in _sessionDirs)
        {
            sessionDir.Delete(true);
        }
    }

    private record EmailSettings
    {
        public string Name { get; init; }
        public string Email { get; init; }
        public string ApiKey { get; init; }
        public long EmailTemplateId { get; init; }
    }
}