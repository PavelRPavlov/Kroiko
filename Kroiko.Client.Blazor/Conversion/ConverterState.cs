using Kroiko.Domain;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Domain.TemplateBuilding;
using Microsoft.Extensions.Logging;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// The one Order of the app (ADR-0005 §4): the uploaded Polyboard file's Details, the manufacturer, its
/// editable <see cref="KroikoFile"/>s, the contact info, the different-edge-colour text, the current
/// <see cref="IOrderFormat.Check"/> problems and the generated order files with their saved flag. It lives as
/// long as the app, so leaving the Converter page keeps the Order; a reload loses it.
/// <para>
/// Components read it directly and re-render on <see cref="Changed"/>. The grids edit the domain details in
/// <see cref="Files"/> in place and then call <see cref="NotifyInputEdited"/>; every input edit discards the
/// generated files without asking (ADR-0005 §7). Reading a file, the confirmation, making the files, generating,
/// the device settings and the downloads do not throw: a failure is logged, raises <see cref="Error"/> with a Bulgarian message
/// where the operator must know, and leaves the Order as it was (ADR-0006 §4).
/// </para>
/// </summary>
public sealed class ConverterState(
    IConfirmation confirmation,
    IDeviceSettingsStore deviceSettings,
    IFileDownloader downloader,
    IFolderPicker folderPicker,
    ILogger<ConverterState> logger)
{
    internal const string DiscardFilesQuestion =
        "Файловете за поръчка и всички редакции по тях ще бъдат изгубени. Да продължа ли?";

    internal const string ReadFailedMessage = "Файлът не можа да бъде прочетен.";
    internal const string ConfirmFailedMessage = "Действието не можа да бъде потвърдено.";
    internal const string CreateFilesFailedMessage = "Файловете за поръчка не можаха да бъдат подготвени.";
    internal const string GenerateFailedMessage = "Бланките за поръчка не можаха да бъдат генерирани.";
    internal const string SaveSettingsFailedMessage =
        "Контактите и производителят не можаха да бъдат запомнени на това устройство.";
    internal const string DownloadFailedMessage = "Файловете за поръчка не можаха да бъдат изтеглени.";
    internal const string FileDownloadFailedMessage = "Файлът за поръчка не можа да бъде изтеглен.";
    internal const string FolderBlockedMessage =
        "Браузърът не позволява запис в папка. Изтеглете файловете с „Изтегли всички“.";
    internal const string FolderSaveFailedMessage =
        "Файловете не можаха да бъдат записани в папката. Изтеглете ги с „Изтегли всички“.";

    private IReadOnlyList<Detail>? _details;
    private string? _companyName;
    private string? _mobileNumber;
    private string? _differentEdgeColor;
    private bool _deviceSettingsLoaded;

    // Counts input changes, so a generation can tell that the input changed while it ran.
    private int _inputVersion;

    /// <summary>Something about the Order changed; re-render.</summary>
    public event Action? Changed;

    /// <summary>An operation failed; show the Bulgarian message in a snackbar.</summary>
    public event Action<string>? Error;

    /// <summary>
    /// The manufacturer the picker starts on when the device remembers none: Lonira, as on the Server, so a first
    /// visit can convert without picking one.
    /// </summary>
    internal static SupportedCompany DefaultManufacturer => SupportedCompanies.Lonira;

    /// <summary>
    /// The manufacturer the files are made for, or <c>null</c> until one is picked or the device settings are
    /// loaded.
    /// </summary>
    public SupportedCompany? Manufacturer { get; private set; }

    /// <summary>A Polyboard file is loaded.</summary>
    public bool IsFileLoaded => _details is not null;

    /// <summary>
    /// The files the operator reviews and edits, from the manufacturer's <see cref="IOrderFormat.CreateFiles"/>.
    /// Empty until both a file and a manufacturer are chosen.
    /// </summary>
    public IReadOnlyList<KroikoFile> Files { get; private set; } = [];

    /// <summary>
    /// Why the last uploaded file was rejected, for the upload panel's alert: up to 10 <c>ред N: …</c> lines
    /// and <c>…и още N</c> (ADR-0006 §2), or that it is empty. Cleared when the next upload starts, so it
    /// never describes an older file.
    /// </summary>
    public IReadOnlyList<string> UploadErrors { get; private set; } = [];

    /// <summary>Why the manufacturer refuses to generate <see cref="Files"/> as they are now (ADR-0006 §3).</summary>
    public IReadOnlyList<OrderProblem> Problems { get; private set; } = [];

    /// <summary>The end customer's company name. Setting it is an input edit.</summary>
    public string? CompanyName
    {
        get => _companyName;
        set => EditInput(ref _companyName, value);
    }

    /// <summary>The end customer's phone number. Setting it is an input edit.</summary>
    public string? MobileNumber
    {
        get => _mobileNumber;
        set => EditInput(ref _mobileNumber, value);
    }

    /// <summary>The "Кантиране с друг цвят" text (Suliver). Setting it is an input edit.</summary>
    public string? DifferentEdgeColor
    {
        get => _differentEdgeColor;
        set => EditInput(ref _differentEdgeColor, value);
    }

    /// <summary>The contact info written into the order files.</summary>
    public ContactInfo Contact => new(CompanyName, MobileNumber);

    /// <summary>A file is being read and parsed.</summary>
    public bool IsUploading { get; private set; }

    /// <summary>The order files are being generated.</summary>
    public bool IsGenerating { get; private set; }

    /// <summary>"Изтегли всички" is triggering the downloads, or a file's link its download.</summary>
    public bool IsDownloading { get; private set; }

    /// <summary>"Запази в папка…" is waiting for the folder picker or writing the files.</summary>
    public bool IsSavingToFolder { get; private set; }

    /// <summary>
    /// The generated files are being downloaded or saved to a folder: neither starts again, and nothing is generated,
    /// until it is done.
    /// </summary>
    public bool IsSaving => IsDownloading || IsSavingToFolder;

    /// <summary>
    /// "Генерирай бланки за поръчка" is allowed: there are files, both contact fields are filled (not just
    /// whitespace) and <see cref="Problems"/> is empty (ADR-0005 §6, ADR-0006 §3), and nothing is being generated
    /// or saved.
    /// </summary>
    public bool CanGenerate =>
        Files.Count > 0
        && !string.IsNullOrWhiteSpace(CompanyName)
        && !string.IsNullOrWhiteSpace(MobileNumber)
        && Problems.Count == 0
        && !IsGenerating
        && !IsSaving;

    /// <summary>The name <paramref name="file"/> is saved under: its <see cref="FileNameSanitizer"/> name (ADR-0003 §7).</summary>
    public static string SavedFileName(FileSaveContext file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return FileNameSanitizer.Sanitize(file.FileName);
    }

    /// <summary>The order files of the last generation; cleared by any change to the input.</summary>
    public IReadOnlyList<FileSaveContext> GeneratedFiles { get; private set; } = [];

    /// <summary>
    /// The last folder save of the generated files, for the confirmation; <c>null</c> until one succeeds, and
    /// cleared with the generated files.
    /// </summary>
    public FolderSave? FolderSave { get; private set; }

    /// <summary>
    /// The generated files were saved since they were generated: a folder save succeeded or at least one download
    /// was triggered (ADR-0003 §8).
    /// </summary>
    public bool IsSaved { get; private set; }

    /// <summary>
    /// A reload would lose work: a file is loaded, and its current input has not been generated and saved
    /// (ADR-0002 §2, ADR-0005 §7, ADR-0003 §8).
    /// </summary>
    public bool HasUnsavedWork => IsFileLoaded && !(GeneratedFiles.Count > 0 && IsSaved);

    /// <summary>
    /// Fills what the operator has not chosen yet — the contact fields and the manufacturer — from the device
    /// settings (ADR-0005 §6). A device that remembers no manufacturer starts on <see cref="DefaultManufacturer"/>.
    /// Runs once per app start, so returning to the Converter page keeps the Order as the operator left it;
    /// settings that cannot be loaded count as a device that remembers nothing, and are not tried again.
    /// </summary>
    public async Task LoadDeviceSettingsAsync()
    {
        if (_deviceSettingsLoaded)
        {
            return;
        }

        _deviceSettingsLoaded = true;
        DeviceSettings settings;
        try
        {
            settings = await deviceSettings.LoadAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The device settings could not be loaded.");
            settings = DeviceSettings.Default;
        }

        var changed = false;
        if (_companyName is null && settings.Contact.CompanyName is not null)
        {
            _companyName = settings.Contact.CompanyName;
            changed = true;
        }

        if (_mobileNumber is null && settings.Contact.MobileNumber is not null)
        {
            _mobileNumber = settings.Contact.MobileNumber;
            changed = true;
        }

        // With no manufacturer there are no files, so making them for the remembered one discards nothing.
        if (Manufacturer is null)
        {
            Manufacturer = settings.Manufacturer ?? DefaultManufacturer;
            Files = CreateFiles(Manufacturer, _details);
            changed = true;
        }

        if (changed)
        {
            InputChanged();
        }
    }

    /// <summary>
    /// Reads and parses a Polyboard file and, if it is good, makes it the Order's file. A file with bad lines
    /// or no Details is rejected into <see cref="UploadErrors"/>; when files exist the operator is asked
    /// before they are discarded. A rejected or declined file changes nothing else, and neither does an upload
    /// started while another is being read.
    /// </summary>
    /// <returns><c>true</c> when the file was loaded.</returns>
    public async Task<bool> UploadAsync(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (IsUploading)
        {
            return false;
        }

        ParseResult result;
        IsUploading = true;
        UploadErrors = [];
        OnChanged();
        try
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            result = PolyboardParser.Parse(buffer.ToArray());
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The uploaded file could not be read.");
            OnError(ReadFailedMessage);
            return false;
        }
        finally
        {
            IsUploading = false;
            OnChanged();
        }

        if (result.Errors.Count > 0 || result.Details.Count == 0)
        {
            UploadErrors = result.Errors.Count > 0 ? UploadErrorText.Describe(result.Errors) : [UploadErrorText.EmptyFile];
            OnChanged();
            return false;
        }

        return await ReplaceFilesAsync(Manufacturer, result.Details);
    }

    /// <summary>
    /// Makes the files for <paramref name="manufacturer"/>. When files exist the operator is asked first.
    /// </summary>
    /// <returns><c>false</c> when the operator said no (the picker should show the current manufacturer again).</returns>
    public Task<bool> SelectManufacturerAsync(SupportedCompany manufacturer)
    {
        ArgumentNullException.ThrowIfNull(manufacturer);
        return manufacturer == Manufacturer ? Task.FromResult(true) : ReplaceFilesAsync(manufacturer, _details);
    }

    /// <summary>
    /// A grid cell or a file name in <see cref="Files"/> was edited in place: the generated
    /// files are discarded and <see cref="IOrderFormat.Check"/> runs again.
    /// </summary>
    public void NotifyInputEdited() => InputChanged();

    /// <summary>
    /// The MegaTrading material rename (ADR-0005 §3): rewrites <c>Material</c> on the details of
    /// <paramref name="file"/> whose material is a key of <paramref name="newNames"/> (old → new name), as typed,
    /// as on the Server. Each detail is matched by its material before the rename, so renames never chain and two
    /// materials can swap. Renaming anything is an input edit; a file no longer in <see cref="Files"/> (a tab of an
    /// older Order) is left alone.
    /// </summary>
    public void RenameMaterials(KroikoFile file, IReadOnlyDictionary<string, string> newNames)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(newNames);
        if (!Files.Contains(file))
        {
            return;
        }

        var renamed = false;
        foreach (var detail in file.Details)
        {
            if (detail.Material is not null
                && newNames.TryGetValue(detail.Material, out var newName)
                && newName != detail.Material)
            {
                detail.Material = newName;
                renamed = true;
            }
        }

        if (renamed)
        {
            InputChanged();
        }
    }

    /// <summary>
    /// Generates the order files, if <see cref="CanGenerate"/>, and remembers the contacts and the manufacturer
    /// on this device. An edit made while it runs wins: nothing is generated from the old input.
    /// </summary>
    public async Task GenerateAsync()
    {
        if (!CanGenerate)
        {
            return;
        }

        var manufacturer = Manufacturer!;
        var contact = Contact;
        var inputVersion = _inputVersion;
        IsGenerating = true;
        OnChanged();
        try
        {
            // Let the spinner render before the synchronous generation takes the thread.
            await Task.Yield();
            if (inputVersion != _inputVersion)
            {
                return;
            }

            GeneratedFiles = OrderFormats.For(manufacturer).Generate(contact, Files, DifferentEdgeColor ?? string.Empty);
            IsSaved = false;
            FolderSave = null;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The order files could not be generated for {Manufacturer}.", manufacturer.Name);
            OnError(GenerateFailedMessage);
            return;
        }
        finally
        {
            IsGenerating = false;
            OnChanged();
        }

        try
        {
            await deviceSettings.SaveAsync(new DeviceSettings(contact, manufacturer));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The device settings could not be saved.");
            OnError(SaveSettingsFailedMessage);
        }
    }

    /// <summary>
    /// "Изтегли всички" (ADR-0003 §5): triggers the download of every generated file, one after another, each under
    /// <see cref="SavedFileName"/>. The first triggered download marks the files saved
    /// (ADR-0003 §8). An edit meanwhile stops the downloads of the files it discarded; a download that cannot start
    /// stops the rest and raises <see cref="Error"/>. Nothing happens with no generated files or while it runs.
    /// </summary>
    public async Task DownloadAllAsync()
    {
        var files = GeneratedFiles;
        if (files.Count == 0 || IsSaving)
        {
            return;
        }

        IsDownloading = true;
        OnChanged();
        try
        {
            foreach (var file in files)
            {
                await downloader.DownloadAsync(SavedFileName(file), file.Content);
                if (GeneratedFiles != files)
                {
                    return;
                }

                IsSaved = true;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The order files could not be downloaded.");
            OnError(DownloadFailedMessage);
        }
        finally
        {
            IsDownloading = false;
            OnChanged();
        }
    }

    /// <summary>
    /// A file's link in the generated list (ADR-0003 §5): triggers the download of <paramref name="file"/> under
    /// <see cref="SavedFileName"/>, which marks the files saved (ADR-0003 §8). A download that cannot start raises
    /// <see cref="Error"/>; an edit meanwhile leaves the new input unsaved. Nothing happens for a file that is not
    /// one of <see cref="GeneratedFiles"/> (a link of discarded files) or while <see cref="IsSaving"/>.
    /// </summary>
    public async Task DownloadAsync(FileSaveContext file)
    {
        var files = GeneratedFiles;
        if (!files.Contains(file) || IsSaving)
        {
            return;
        }

        IsDownloading = true;
        OnChanged();
        try
        {
            await downloader.DownloadAsync(SavedFileName(file), file.Content);
            if (GeneratedFiles == files)
            {
                IsSaved = true;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An order file could not be downloaded.");
            OnError(FileDownloadFailedMessage);
        }
        finally
        {
            IsDownloading = false;
            OnChanged();
        }
    }

    /// <summary>
    /// "Запази в папка…" (ADR-0003 §2, §4, §6): opens the folder picker before awaiting anything, so it keeps the click's
    /// user activation, and writes every generated file into the picked folder under a name that is not taken there
    /// (<see cref="ClashNaming"/>); then marks the files saved (ADR-0003 §8) and keeps the final names in
    /// <see cref="FolderSave"/>. A cancelled picker changes nothing. A blocked picker, or a folder that cannot be
    /// listed or written, raises <see cref="Error"/> pointing to the downloads and clears the confirmation of an
    /// earlier save; files already written stay. An edit meanwhile stops the writes of the files it discarded. Nothing
    /// happens with no generated files or while <see cref="IsSaving"/>.
    /// </summary>
    public async Task SaveToFolderAsync()
    {
        var files = GeneratedFiles;
        if (files.Count == 0 || IsSaving)
        {
            return;
        }

        IsSavingToFolder = true;
        try
        {
            // The picker is opened first, before the busy state renders, so nothing delays it (ADR-0003 §2).
            var picking = folderPicker.PickAsync();
            OnChanged();
            var pick = await picking;
            if (pick.Outcome == FolderPickOutcome.Blocked)
            {
                FolderSave = null;
                OnError(FolderBlockedMessage);
                return;
            }

            if (pick.Folder is null)
            {
                // Cancelled: nothing happens.
                return;
            }

            await using var folder = pick.Folder;
            if (GeneratedFiles != files)
            {
                return;
            }

            var existingNames = await folder.ListNamesAsync();
            var finalNames = ClashNaming.FinalNames(files.Select(SavedFileName).ToList(), existingNames);
            for (var i = 0; i < files.Count; i++)
            {
                await folder.WriteFileAsync(finalNames[i], files[i].Content);
                if (GeneratedFiles != files)
                {
                    return;
                }
            }

            IsSaved = true;
            FolderSave = new FolderSave(folder.Name, finalNames);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The order files could not be saved to a folder.");
            FolderSave = null;
            OnError(FolderSaveFailedMessage);
        }
        finally
        {
            IsSavingToFolder = false;
            OnChanged();
        }
    }

    // Makes the files for a new manufacturer or new Details, asking first when that discards files.
    private async Task<bool> ReplaceFilesAsync(SupportedCompany? manufacturer, IReadOnlyList<Detail>? details)
    {
        try
        {
            if (Files.Count > 0 && !await confirmation.ConfirmAsync(DiscardFilesQuestion))
            {
                return false;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The operator could not be asked before the files were discarded.");
            OnError(ConfirmFailedMessage);
            return false;
        }

        IReadOnlyList<KroikoFile> files;
        try
        {
            files = CreateFiles(manufacturer, details);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The files could not be made for {Manufacturer}.", manufacturer?.Name);
            OnError(CreateFilesFailedMessage);
            return false;
        }

        Manufacturer = manufacturer;
        _details = details;
        Files = files;
        InputChanged();
        return true;
    }

    private static IReadOnlyList<KroikoFile> CreateFiles(SupportedCompany? manufacturer, IReadOnlyList<Detail>? details) =>
        manufacturer is null || details is null ? [] : OrderFormats.For(manufacturer).CreateFiles(details);

    private void EditInput(ref string? field, string? value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        InputChanged();
    }

    // The input changed: the generated files no longer match it (ADR-0005 §7), and Check runs again (ADR-0006 §3).
    private void InputChanged()
    {
        _inputVersion++;
        GeneratedFiles = [];
        IsSaved = false;
        FolderSave = null;
        Problems = Manufacturer is null ? [] : OrderFormats.For(Manufacturer).Check(Files);
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke();

    private void OnError(string message) => Error?.Invoke(message);
}
