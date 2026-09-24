using Kroiko.Domain;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Domain.TemplateBuilding;

namespace Kroiko.Client.Blazor.Conversion;

/// <summary>
/// The one Order of the app (ADR-0005 §4): the uploaded Polyboard file's Details, the manufacturer, its
/// editable <see cref="KroikoFile"/>s, the contact info, the different-edge-colour text, the current
/// <see cref="IOrderFormat.Check"/> problems and the generated order files with their saved flag. It lives as
/// long as the app, so leaving the Converter page keeps the Order; a reload loses it.
/// <para>
/// Components read it directly and re-render on <see cref="Changed"/>. The grids edit the domain details in
/// <see cref="Files"/> in place and then call <see cref="NotifyInputEdited"/>; every input edit discards the
/// generated files without asking (ADR-0005 §7). Operations never throw: a failure raises <see cref="Error"/>
/// with a Bulgarian message and leaves the Order as it was (ADR-0006 §4).
/// </para>
/// </summary>
public sealed class ConverterState(IConfirmation confirmation, IDeviceSettingsStore deviceSettings)
{
    internal const string DiscardFilesQuestion =
        "Файловете за поръчка и всички редакции по тях ще бъдат изгубени. Да продължа ли?";

    internal const string ReadFailedMessage = "Файлът не можа да бъде прочетен.";
    internal const string CreateFilesFailedMessage = "Файловете за поръчка не можаха да бъдат подготвени.";
    internal const string GenerateFailedMessage = "Бланките за поръчка не можаха да бъдат генерирани.";
    internal const string SaveSettingsFailedMessage =
        "Контактите и производителят не можаха да бъдат запомнени на това устройство.";

    private IReadOnlyList<Detail>? _details;
    private string? _companyName;
    private string? _mobileNumber;
    private string? _differentEdgeColor;
    private bool _deviceSettingsLoaded;

    /// <summary>Something about the Order changed; re-render.</summary>
    public event Action? Changed;

    /// <summary>An operation failed; show the Bulgarian message in a snackbar.</summary>
    public event Action<string>? Error;

    /// <summary>The manufacturer the files are made for, or <c>null</c> until one is picked.</summary>
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
    /// and <c>…и още N</c> (ADR-0006 §2), or that it is empty. Empty once a file is loaded.
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

    /// <summary>
    /// "Генерирай бланки за поръчка" is allowed: there are files, both contact fields are filled (not just
    /// whitespace) and <see cref="Problems"/> is empty (ADR-0005 §6, ADR-0006 §3).
    /// </summary>
    public bool CanGenerate =>
        Files.Count > 0
        && !string.IsNullOrWhiteSpace(CompanyName)
        && !string.IsNullOrWhiteSpace(MobileNumber)
        && Problems.Count == 0
        && !IsGenerating;

    /// <summary>The order files of the last generation; cleared by any change to the input.</summary>
    public IReadOnlyList<FileSaveContext> GeneratedFiles { get; private set; } = [];

    /// <summary>
    /// The generated files were saved since they were generated: at least one download was triggered
    /// (ADR-0003 §8).
    /// </summary>
    public bool IsSaved { get; private set; }

    /// <summary>
    /// A reload would lose work: a file is loaded, and its current input has not been generated and saved
    /// (ADR-0002 §2, ADR-0005 §7, ADR-0003 §8).
    /// </summary>
    public bool HasUnsavedWork => IsFileLoaded && !(GeneratedFiles.Count > 0 && IsSaved);

    /// <summary>
    /// Fills the contacts and the manufacturer from the device settings, once per app start, so returning to
    /// the Converter page keeps the Order as the operator left it.
    /// </summary>
    public async Task LoadDeviceSettingsAsync()
    {
        if (_deviceSettingsLoaded)
        {
            return;
        }

        _deviceSettingsLoaded = true;
        var settings = await deviceSettings.LoadAsync();
        _companyName = settings.Contact.CompanyName;
        _mobileNumber = settings.Contact.MobileNumber;
        Manufacturer = settings.Manufacturer;
        OrderChanged();
    }

    /// <summary>
    /// Reads and parses a Polyboard file and, if it is good, makes it the Order's file. A file with bad lines
    /// or no Details is rejected into <see cref="UploadErrors"/>; when files exist the operator is asked
    /// before they are discarded. A rejected or declined file changes nothing else.
    /// </summary>
    /// <returns><c>true</c> when the file was loaded.</returns>
    public async Task<bool> UploadAsync(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        ParseResult result;
        IsUploading = true;
        OnChanged();
        try
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);
            result = PolyboardParser.Parse(buffer.ToArray());
        }
        catch (Exception)
        {
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

        return await ReplaceFilesAsync(Manufacturer, result.Details, () => UploadErrors = []);
    }

    /// <summary>
    /// Makes the files for <paramref name="manufacturer"/>. When files exist the operator is asked first.
    /// </summary>
    /// <returns><c>false</c> when the operator said no (the picker should show the current manufacturer again).</returns>
    public Task<bool> SelectManufacturerAsync(SupportedCompany manufacturer)
    {
        ArgumentNullException.ThrowIfNull(manufacturer);
        return manufacturer == Manufacturer
            ? Task.FromResult(true)
            : ReplaceFilesAsync(manufacturer, _details, () => { });
    }

    /// <summary>
    /// A grid cell, a material rename or a file name in <see cref="Files"/> was edited in place: the generated
    /// files are discarded and <see cref="IOrderFormat.Check"/> runs again.
    /// </summary>
    public void NotifyInputEdited() => OrderChanged();

    /// <summary>
    /// Generates the order files, if <see cref="CanGenerate"/>, and remembers the contacts and the manufacturer
    /// on this device.
    /// </summary>
    public async Task GenerateAsync()
    {
        if (!CanGenerate)
        {
            return;
        }

        var manufacturer = Manufacturer!;
        var contact = Contact;
        IsGenerating = true;
        OnChanged();
        try
        {
            // Let the spinner render before the synchronous generation takes the thread.
            await Task.Yield();
            GeneratedFiles = OrderFormats.For(manufacturer).Generate(contact, Files, DifferentEdgeColor ?? string.Empty);
            IsSaved = false;
        }
        catch (Exception)
        {
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
        catch (Exception)
        {
            OnError(SaveSettingsFailedMessage);
        }
    }

    /// <summary>A download of the generated files was triggered (ADR-0003 §8); ignored when there are none.</summary>
    public void MarkSaved()
    {
        IsSaved = GeneratedFiles.Count > 0;
        OnChanged();
    }

    // Rebuilds the files for a new manufacturer or new Details, asking first when that discards files.
    private async Task<bool> ReplaceFilesAsync(SupportedCompany? manufacturer, IReadOnlyList<Detail>? details, Action onReplaced)
    {
        try
        {
            if (Files.Count > 0 && !await confirmation.ConfirmAsync(DiscardFilesQuestion))
            {
                return false;
            }

            var files = manufacturer is null || details is null ? [] : OrderFormats.For(manufacturer).CreateFiles(details);
            Manufacturer = manufacturer;
            _details = details;
            Files = files;
            onReplaced();
        }
        catch (Exception)
        {
            OnError(CreateFilesFailedMessage);
            return false;
        }

        OrderChanged();
        return true;
    }

    private void EditInput(ref string? field, string? value)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OrderChanged();
    }

    // The input changed: the generated files no longer match it (ADR-0005 §7), and Check runs again (ADR-0006 §3).
    private void OrderChanged()
    {
        GeneratedFiles = [];
        IsSaved = false;
        Problems = Manufacturer is null ? [] : OrderFormats.For(Manufacturer).Check(Files);
        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke();

    private void OnError(string message) => Error?.Invoke(message);
}
