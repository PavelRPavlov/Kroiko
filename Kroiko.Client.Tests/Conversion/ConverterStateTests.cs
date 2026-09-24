using FluentAssertions;
using Kroiko.Client.Blazor.Conversion;
using Kroiko.Domain;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.TemplateBuilding;
using Kroiko.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kroiko.Client.Tests.Conversion;

/// <summary>
/// The PWA's Order rules, through <see cref="ConverterState"/>'s public API with the confirmation and the
/// device settings faked (ADR-0007 §4; docs/implementation/04-conversion-flow.md, step 1). No browser.
/// </summary>
public sealed class ConverterStateTests
{
    private readonly FakeConfirmation _confirmation = new();
    private readonly FakeDeviceSettingsStore _settings = new();
    private readonly ConverterState _state;

    public ConverterStateTests()
    {
        _state = new ConverterState(_confirmation, _settings, NullLogger<ConverterState>.Instance);
    }

    [Fact]
    public async Task Uploading_a_file_for_a_manufacturer_makes_its_files()
    {
        await _state.SelectManufacturerAsync(SupportedCompanies.Lonira);

        var loaded = await _state.UploadAsync(Fixture("wardrobes-4-materials"));

        loaded.Should().BeTrue();
        _state.IsFileLoaded.Should().BeTrue();
        _state.Files.Select(f => f.FileName).Should().BeEquivalentTo(
            "AGT White", "Basic white W908 ST2", "H3170 Dyb kendyl natur", "HDF 3 mm");
    }

    [Fact]
    public async Task A_file_with_bad_lines_loads_nothing_and_lists_the_first_ten()
    {
        var loaded = await _state.UploadAsync(Fixture("bad-lines-12"));

        loaded.Should().BeFalse();
        _state.IsFileLoaded.Should().BeFalse();
        _state.UploadErrors.Should().Equal(
            "ред 2: 10 полета, очакват се 11 или 23",
            "ред 3: полето „брой“ не е число",
            "ред 5: 10 полета, очакват се 11 или 23",
            "ред 6: полето „брой“ не е число",
            "ред 8: 10 полета, очакват се 11 или 23",
            "ред 9: полето „брой“ не е число",
            "ред 11: 10 полета, очакват се 11 или 23",
            "ред 12: полето „брой“ не е число",
            "ред 14: 10 полета, очакват се 11 или 23",
            "ред 15: полето „брой“ не е число",
            "…и още 2");
    }

    [Fact]
    public async Task A_rejected_file_leaves_the_Order_as_it_was()
    {
        await _state.SelectManufacturerAsync(SupportedCompanies.Lonira);
        await _state.UploadAsync(Fixture("wardrobes-4-materials"));
        var files = _state.Files;

        await _state.UploadAsync(Fixture("bad-lines-12"));

        _state.IsFileLoaded.Should().BeTrue();
        _state.Files.Should().BeSameAs(files);
        _state.UploadErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_good_file_clears_the_previous_upload_errors()
    {
        await _state.UploadAsync(Fixture("bad-lines-12"));

        await _state.UploadAsync(Fixture("wardrobes-4-materials"));

        _state.UploadErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task The_upload_errors_describe_only_the_last_file_so_a_declined_upload_clears_them()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        await _state.UploadAsync(Fixture("bad-lines-12"));
        _confirmation.Answer = false;

        await _state.UploadAsync(Fixture("kitchen-8-materials"));

        _state.UploadErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_with_no_Details_and_no_errors_is_rejected_as_empty()
    {
        var loaded = await _state.UploadAsync(new MemoryStream("\r\n  \r\n"u8.ToArray()));

        loaded.Should().BeFalse();
        _state.IsFileLoaded.Should().BeFalse();
        _state.UploadErrors.Should().Equal("Файлът не съдържа детайли.");
    }

    [Fact]
    public async Task Switching_manufacturer_when_files_exist_asks_and_no_changes_nothing()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        var files = _state.Files;
        _confirmation.Answer = false;

        var switched = await _state.SelectManufacturerAsync(SupportedCompanies.Suliver);

        switched.Should().BeFalse();
        _confirmation.Questions.Should().ContainSingle();
        _state.Manufacturer.Should().Be(SupportedCompanies.Lonira);
        _state.Files.Should().BeSameAs(files);
    }

    [Fact]
    public async Task Switching_manufacturer_when_files_exist_and_the_operator_agrees_rebuilds_the_files()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");

        var switched = await _state.SelectManufacturerAsync(SupportedCompanies.Suliver);

        switched.Should().BeTrue();
        _confirmation.Questions.Should().ContainSingle();
        _state.Manufacturer.Should().Be(SupportedCompanies.Suliver);
        _state.Files.Should().ContainSingle().Which.Details.Should().AllBeOfType<SuliverDetail>();
    }

    [Fact]
    public async Task Picking_a_manufacturer_after_the_upload_makes_the_files_without_asking()
    {
        await _state.UploadAsync(Fixture("wardrobes-4-materials"));

        await _state.SelectManufacturerAsync(SupportedCompanies.Lonira);

        _confirmation.Questions.Should().BeEmpty();
        _state.Files.Should().HaveCount(4);
    }

    [Fact]
    public async Task Picking_the_current_manufacturer_again_asks_nothing_and_keeps_the_files()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        var files = _state.Files;

        var selected = await _state.SelectManufacturerAsync(SupportedCompanies.Lonira);

        selected.Should().BeTrue();
        _confirmation.Questions.Should().BeEmpty();
        _state.Files.Should().BeSameAs(files);
    }

    [Fact]
    public async Task Uploading_again_when_files_exist_asks_and_no_changes_nothing()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        var files = _state.Files;
        _confirmation.Answer = false;

        var loaded = await _state.UploadAsync(Fixture("kitchen-8-materials"));

        loaded.Should().BeFalse();
        _confirmation.Questions.Should().ContainSingle();
        _state.Files.Should().BeSameAs(files);
    }

    [Fact]
    public async Task Uploading_again_when_files_exist_and_the_operator_agrees_loads_the_new_file()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");

        var loaded = await _state.UploadAsync(Fixture("kitchen-8-materials"));

        loaded.Should().BeTrue();
        _confirmation.Questions.Should().ContainSingle();
        _state.Files.Should().HaveCount(8);
    }

    [Fact]
    public async Task Uploading_again_with_no_files_asks_nothing()
    {
        await _state.UploadAsync(Fixture("wardrobes-4-materials"));

        await _state.UploadAsync(Fixture("kitchen-8-materials"));

        _confirmation.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task Filled_contacts_and_no_problems_can_generate()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");

        FillContacts();

        _state.CanGenerate.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "0888123456")]
    [InlineData("", "0888123456")]
    [InlineData("  ", "0888123456")]
    [InlineData("Тест ООД", null)]
    [InlineData("Тест ООД", "")]
    [InlineData("Тест ООД", " \t")]
    public async Task An_empty_contact_field_cannot_generate(string? companyName, string? mobileNumber)
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");

        _state.CompanyName = companyName;
        _state.MobileNumber = mobileNumber;

        _state.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public void No_files_cannot_generate()
    {
        FillContacts();

        _state.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public async Task More_than_six_MegaTrading_materials_is_a_problem_that_cannot_generate()
    {
        await LoadAsync(SupportedCompanies.MegaTrading, "kitchen-8-materials");
        FillContacts();

        _state.Problems.Should().ContainSingle().Which.Should().BeOfType<TooManyMaterials>()
            .Which.Materials.Should().HaveCount(8);
        _state.CanGenerate.Should().BeFalse();
    }

    [Fact]
    public async Task Check_runs_again_on_an_edit()
    {
        await LoadAsync(SupportedCompanies.MegaTrading, "kitchen-8-materials");
        FillContacts();

        // The material rename row: two of the eight materials become one another.
        foreach (var detail in _state.Files.SelectMany(f => f.Details).Where(d => d.Material is "Mirror" or "Med"))
        {
            detail.Material = "Lemon sorbet";
        }

        _state.NotifyInputEdited();

        _state.Problems.Should().BeEmpty();
        _state.CanGenerate.Should().BeTrue();
    }

    [Fact]
    public async Task Renaming_materials_rewrites_the_matching_details_and_runs_Check_again()
    {
        await LoadAsync(SupportedCompanies.MegaTrading, "kitchen-8-materials");
        FillContacts();

        _state.RenameMaterials(new Dictionary<string, string> { ["Mirror"] = "Lemon sorbet", ["Med"] = "Lemon sorbet" });

        var materials = _state.Files.SelectMany(f => f.Details).Select(d => d.Material).ToList();
        materials.Should().NotContain(["Mirror", "Med"]);
        materials.Count(m => m == "Lemon sorbet").Should().Be(2 + 8 + 1);
        _state.Problems.Should().BeEmpty();
        _state.CanGenerate.Should().BeTrue();
    }

    [Fact]
    public async Task A_rename_maps_from_the_names_before_it_so_two_materials_can_swap()
    {
        await LoadAsync(SupportedCompanies.MegaTrading, "kitchen-8-materials");

        _state.RenameMaterials(new Dictionary<string, string> { ["Mirror"] = "Med", ["Med"] = "Mirror" });

        var materials = _state.Files.SelectMany(f => f.Details).Select(d => d.Material).ToList();
        materials.Count(m => m == "Mirror").Should().Be(8);
        materials.Count(m => m == "Med").Should().Be(1);
    }

    [Fact]
    public async Task A_new_name_is_trimmed_and_a_blank_or_unchanged_one_renames_nothing()
    {
        await GenerateAsync(SupportedCompanies.MegaTrading, "wardrobes-4-materials");

        _state.RenameMaterials(new Dictionary<string, string> { ["AGT White"] = "  ", ["HDF 3 mm"] = " HDF 3 mm " });

        _state.Files.SelectMany(f => f.Details).Select(d => d.Material).Should().Contain(["AGT White", "HDF 3 mm"]);
        _state.GeneratedFiles.Should().NotBeEmpty("renaming nothing is no edit");

        _state.RenameMaterials(new Dictionary<string, string> { ["AGT White"] = " Бяло " });

        _state.Files.SelectMany(f => f.Details).Select(d => d.Material).Should().Contain("Бяло").And.NotContain("AGT White");
        _state.GeneratedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Generating_makes_the_order_files()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        FillContacts();

        await _state.GenerateAsync();

        _state.GeneratedFiles.Select(f => f.FileName).Should().BeEquivalentTo(
            "AGT White.xlsx", "Basic white W908 ST2.xlsx", "H3170 Dyb kendyl natur.xlsx", "HDF 3 mm.xlsx");
    }

    [Fact]
    public async Task Generating_remembers_the_contacts_and_the_manufacturer_on_the_device()
    {
        await LoadAsync(SupportedCompanies.Suliver, "wardrobes-4-materials");
        FillContacts();

        await _state.GenerateAsync();

        _settings.Stored.Should().Be(
            new DeviceSettings(new ContactInfo("Тест ООД", "0888123456"), SupportedCompanies.Suliver));
    }

    [Fact]
    public async Task Generating_when_it_cannot_generate_does_nothing()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");

        await _state.GenerateAsync();

        _state.GeneratedFiles.Should().BeEmpty();
        _settings.Stored.Should().Be(DeviceSettings.Default);
    }

    [Theory]
    [InlineData("cell")]
    [InlineData("material rename")]
    [InlineData("file name")]
    [InlineData("company name")]
    [InlineData("mobile number")]
    [InlineData("different edge colour")]
    public async Task Any_input_edit_clears_the_generated_files_and_the_saved_flag_without_asking(string edit)
    {
        await GenerateAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        _state.MarkSaved();

        Edit(edit);

        _state.GeneratedFiles.Should().BeEmpty();
        _state.IsSaved.Should().BeFalse();
        _confirmation.Questions.Should().BeEmpty();
    }

    [Fact]
    public async Task Setting_a_contact_field_to_its_current_value_is_no_edit()
    {
        await GenerateAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");

        _state.CompanyName = "Тест ООД";

        _state.GeneratedFiles.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Switching_manufacturer_clears_the_generated_files()
    {
        await GenerateAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");

        await _state.SelectManufacturerAsync(SupportedCompanies.Suliver);

        _state.GeneratedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Uploading_again_clears_the_generated_files()
    {
        await GenerateAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");

        await _state.UploadAsync(Fixture("kitchen-8-materials"));

        _state.GeneratedFiles.Should().BeEmpty();
    }

    [Fact]
    public void An_empty_Order_has_no_unsaved_work()
    {
        _state.HasUnsavedWork.Should().BeFalse();
    }

    [Fact]
    public async Task A_loaded_file_that_was_never_generated_is_unsaved_work()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");

        _state.HasUnsavedWork.Should().BeTrue();
    }

    [Fact]
    public async Task Generated_files_are_unsaved_work_until_a_download_is_triggered()
    {
        await GenerateAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        _state.HasUnsavedWork.Should().BeTrue();

        _state.MarkSaved();

        _state.IsSaved.Should().BeTrue();
        _state.HasUnsavedWork.Should().BeFalse();
    }

    [Fact]
    public async Task An_edit_after_saving_is_unsaved_work_again()
    {
        await GenerateAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        _state.MarkSaved();

        Edit("cell");

        _state.HasUnsavedWork.Should().BeTrue();
    }

    [Fact]
    public async Task Generating_again_after_saving_is_unsaved_work_again()
    {
        await GenerateAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        _state.MarkSaved();

        await _state.GenerateAsync();

        _state.HasUnsavedWork.Should().BeTrue();
    }

    [Fact]
    public async Task Nothing_generated_cannot_be_saved()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");

        _state.MarkSaved();

        _state.IsSaved.Should().BeFalse();
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("manufacturer")]
    [InlineData("cell")]
    [InlineData("company name")]
    [InlineData("generate")]
    [InlineData("save")]
    public async Task Every_change_raises_Changed_once_the_state_has_changed(string change)
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        FillContacts();
        if (change == "save")
        {
            await _state.GenerateAsync();
        }

        var seen = new List<string>();
        _state.Changed += () => seen.Add(Snapshot());

        switch (change)
        {
            case "upload": await _state.UploadAsync(Fixture("kitchen-8-materials")); break;
            case "manufacturer": await _state.SelectManufacturerAsync(SupportedCompanies.Suliver); break;
            case "generate": await _state.GenerateAsync(); break;
            case "save": _state.MarkSaved(); break;
            default: Edit(change); break;
        }

        // The last notification saw the final state, so a component re-rendering on it shows the change.
        seen.Should().NotBeEmpty().And.EndWith(Snapshot());
    }

    private string Snapshot() =>
        $"{_state.Manufacturer?.Name}|{_state.Files.Count}|{_state.Files[0].Details[0].Note}|{_state.CompanyName}" +
        $"|{_state.GeneratedFiles.Count}|{_state.IsSaved}|{_state.IsUploading}|{_state.IsGenerating}";

    [Fact]
    public async Task Uploading_is_busy_while_the_file_is_read_and_parsed()
    {
        var busy = new List<bool>();
        _state.Changed += () => busy.Add(_state.IsUploading);

        await _state.UploadAsync(Fixture("wardrobes-4-materials"));

        busy.Should().StartWith(true).And.EndWith(false);
        _state.IsUploading.Should().BeFalse();
    }

    [Fact]
    public async Task Generating_is_busy_until_it_is_done()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        FillContacts();
        var busy = new List<bool>();
        _state.Changed += () => busy.Add(_state.IsGenerating);

        await _state.GenerateAsync();

        busy.Should().StartWith(true).And.EndWith(false);
        _state.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_raises_an_error_and_leaves_the_Order_as_it_was()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        var files = _state.Files;
        var errors = new List<string>();
        _state.Error += errors.Add;

        var loaded = await _state.UploadAsync(new FailingStream());

        loaded.Should().BeFalse();
        errors.Should().Equal("Файлът не можа да бъде прочетен.");
        _state.Files.Should().BeSameAs(files);
        _state.UploadErrors.Should().BeEmpty();
        _state.IsUploading.Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_generation_raises_an_error_and_leaves_the_Order_as_it_was()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        FillContacts();
        var errors = new List<string>();
        _state.Error += errors.Add;
        // A detail the Lonira template cannot write.
        _state.Files[0].Details.Add(new SuliverDetail { Material = "AGT White" });
        _state.NotifyInputEdited();
        var files = _state.Files;

        await _state.GenerateAsync();

        errors.Should().Equal("Бланките за поръчка не можаха да бъдат генерирани.");
        _state.Files.Should().BeSameAs(files);
        _state.Contact.Should().Be(new ContactInfo("Тест ООД", "0888123456"));
        _state.CanGenerate.Should().BeTrue();
        _state.GeneratedFiles.Should().BeEmpty();
        _state.IsGenerating.Should().BeFalse();
        _settings.Stored.Should().Be(DeviceSettings.Default);
    }

    [Fact]
    public async Task A_device_that_cannot_remember_the_settings_still_keeps_the_generated_files()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        FillContacts();
        var errors = new List<string>();
        _state.Error += errors.Add;
        _settings.SaveFailure = new InvalidOperationException("storage is full");

        await _state.GenerateAsync();

        _state.GeneratedFiles.Should().HaveCount(4);
        errors.Should().Equal("Контактите и производителят не можаха да бъдат запомнени на това устройство.");
        _state.IsGenerating.Should().BeFalse();
    }

    [Fact]
    public async Task The_device_settings_fill_the_contacts_and_the_manufacturer()
    {
        _settings.Stored = new DeviceSettings(new ContactInfo("Тест ООД", "0888123456"), SupportedCompanies.MegaTrading);

        await _state.LoadDeviceSettingsAsync();

        _state.CompanyName.Should().Be("Тест ООД");
        _state.MobileNumber.Should().Be("0888123456");
        _state.Manufacturer.Should().Be(SupportedCompanies.MegaTrading);
    }

    [Fact]
    public async Task The_device_settings_are_loaded_once_so_returning_to_the_page_keeps_the_Order()
    {
        _settings.Stored = new DeviceSettings(new ContactInfo("Тест ООД", "0888123456"), SupportedCompanies.MegaTrading);
        await _state.LoadDeviceSettingsAsync();
        await _state.SelectManufacturerAsync(SupportedCompanies.Lonira);
        _state.CompanyName = "Друга ООД";

        await _state.LoadDeviceSettingsAsync();

        _state.CompanyName.Should().Be("Друга ООД");
        _state.Manufacturer.Should().Be(SupportedCompanies.Lonira);
    }

    [Fact]
    public async Task An_edit_while_generating_discards_the_output_of_the_old_input()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        FillContacts();
        _state.Changed += () =>
        {
            if (_state.IsGenerating)
            {
                _state.CompanyName = "Друга ООД";
            }
        };

        await _state.GenerateAsync();

        _state.GeneratedFiles.Should().BeEmpty();
        _state.IsGenerating.Should().BeFalse();
        _settings.Stored.Should().Be(DeviceSettings.Default);
    }

    [Fact]
    public async Task A_second_upload_while_one_is_being_read_is_ignored()
    {
        Task<bool>? second = null;
        _state.Changed += () =>
        {
            if (_state.IsUploading && second is null)
            {
                second = _state.UploadAsync(Fixture("kitchen-8-materials"));
            }
        };

        await _state.UploadAsync(Fixture("wardrobes-4-materials"));

        (await second!).Should().BeFalse();
        await _state.SelectManufacturerAsync(SupportedCompanies.Lonira);
        _state.Files.Should().HaveCount(4);
    }

    [Fact]
    public async Task A_confirmation_that_fails_changes_nothing()
    {
        await LoadAsync(SupportedCompanies.Lonira, "wardrobes-4-materials");
        var files = _state.Files;
        var errors = new List<string>();
        _state.Error += errors.Add;
        _confirmation.Failure = new InvalidOperationException("the dialog could not open");

        var switched = await _state.SelectManufacturerAsync(SupportedCompanies.Suliver);

        switched.Should().BeFalse();
        errors.Should().Equal("Действието не можа да бъде потвърдено.");
        _state.Manufacturer.Should().Be(SupportedCompanies.Lonira);
        _state.Files.Should().BeSameAs(files);
    }

    [Fact]
    public async Task Device_settings_that_cannot_be_loaded_leave_the_Order_empty()
    {
        _settings.LoadFailure = new InvalidOperationException("storage is blocked");

        await _state.Invoking(s => s.LoadDeviceSettingsAsync()).Should().NotThrowAsync();

        _state.CompanyName.Should().BeNull();
        _state.Manufacturer.Should().BeNull();
    }

    [Fact]
    public async Task Device_settings_loaded_after_an_upload_make_the_files_for_the_remembered_manufacturer()
    {
        _settings.Stored = new DeviceSettings(new ContactInfo("Тест ООД", "0888123456"), SupportedCompanies.Lonira);
        await _state.UploadAsync(Fixture("wardrobes-4-materials"));

        await _state.LoadDeviceSettingsAsync();

        _state.Files.Should().HaveCount(4);
    }

    [Fact]
    public async Task Device_settings_never_overwrite_what_the_operator_already_chose()
    {
        _settings.Stored = new DeviceSettings(new ContactInfo("Тест ООД", "0888123456"), SupportedCompanies.MegaTrading);
        await _state.SelectManufacturerAsync(SupportedCompanies.Lonira);
        _state.CompanyName = "Друга ООД";

        await _state.LoadDeviceSettingsAsync();

        _state.Manufacturer.Should().Be(SupportedCompanies.Lonira);
        _state.CompanyName.Should().Be("Друга ООД");
        _state.MobileNumber.Should().Be("0888123456");
    }

    private void Edit(string edit)
    {
        var file = _state.Files[0];
        switch (edit)
        {
            case "cell":
                file.Details[0].Note = "ръчна бележка";
                _state.NotifyInputEdited();
                break;
            case "material rename":
                file.Details.ForEach(d => d.Material = "Нов материал");
                _state.NotifyInputEdited();
                break;
            case "file name":
                file.FileName = "Нов материал";
                _state.NotifyInputEdited();
                break;
            case "company name":
                _state.CompanyName = "Друга ООД";
                break;
            case "mobile number":
                _state.MobileNumber = "0888000000";
                break;
            case "different edge colour":
                _state.DifferentEdgeColor = "бял кант";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit), edit, null);
        }
    }

    private async Task GenerateAsync(SupportedCompany manufacturer, string fixture)
    {
        await LoadAsync(manufacturer, fixture);
        FillContacts();
        await _state.GenerateAsync();
        _state.GeneratedFiles.Should().NotBeEmpty();
    }

    private void FillContacts()
    {
        _state.CompanyName = "Тест ООД";
        _state.MobileNumber = "0888123456";
    }

    private async Task LoadAsync(SupportedCompany manufacturer, string fixture)
    {
        await _state.SelectManufacturerAsync(manufacturer);
        (await _state.UploadAsync(Fixture(fixture))).Should().BeTrue();
    }

    private static Stream Fixture(string name) => new MemoryStream(File.ReadAllBytes(TestData.Polyboard(name)));
}
