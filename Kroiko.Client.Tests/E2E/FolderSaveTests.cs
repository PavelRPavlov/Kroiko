using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Testing;
using Microsoft.Playwright;
using Xunit;
using static Kroiko.Client.Tests.E2E.ConverterPage;
using static Microsoft.Playwright.Assertions;

namespace Kroiko.Client.Tests.E2E;

/// <summary>
/// "Запази в папка…" on the Converter page (docs/implementation/05-saving.md, step 2; ADR-0003 §2–4, §6). Playwright
/// cannot drive the browser's folder picker (ADR-0007 §5), so <c>showDirectoryPicker</c> is replaced by a stub that
/// returns a folder of the origin-private file system (<c>navigator.storage.getDirectory()</c>): everything after the
/// pick — the last folder in IndexedDB, listing, clash naming, writing, the confirmation — runs for real in Chromium.
/// The real picker, in an installed Edge window, is a manual release check.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
public sealed class FolderSaveTests(PublishedApp app)
{
    private const string FolderName = "Поръчки";

    // Replaces showDirectoryPicker before the app starts. Each call is recorded in window.folderPicks: whether the
    // page still had the click's user activation, the mode, and whether startIn was the stub's folder. Setting
    // window.pickerError to a DOMException name makes the next pick fail with it instead.
    private const string PickerStub = $$"""
        window.folderPicks = [];
        window.pickerError = null;
        window.showDirectoryPicker = async (options) => {
            const pick = { activation: navigator.userActivation.isActive, mode: options?.mode ?? null, startInIsLastFolder: null };
            window.folderPicks.push(pick);
            if (window.pickerError) {
                const name = window.pickerError;
                window.pickerError = null;
                throw new DOMException('The stub picker failed.', name);
            }

            const folder = await (await navigator.storage.getDirectory()).getDirectoryHandle('{{FolderName}}', { create: true });
            pick.startInIsLastFolder = options?.startIn ? await options.startIn.isSameEntry(folder) : null;
            return folder;
        };
        """;

    [Fact]
    public async Task Saving_to_a_folder_writes_the_order_files_never_overwrites_and_opens_at_the_last_folder()
    {
        // A profile on disk: an off-the-record context's page crashes when the app reads the last folder's handle back.
        await using var browser = await app.NewPersistentContextAsync("bg-BG");
        await browser.Context.AddInitScriptAsync(PickerStub);
        var page = browser.Context.Pages.Count > 0 ? browser.Context.Pages[0] : await browser.Context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        // The golden helper's contacts, so the saved files are the Server's order files.
        await GenerateAsGoldenAsync(page, "wardrobes-4-materials");
        await SaveButton(page).ClickAsync();

        string[] names = ["Basic white W908 ST2.xlsx", "HDF 3 mm.xlsx", "AGT White.xlsx", "H3170 Dyb kendyl natur.xlsx"];
        await Expect(Confirmation(page)).ToContainTextAsync($"Файловете са записани в папка „{FolderName}“:");
        await Expect(Confirmation(page).GetByRole(AriaRole.Listitem)).ToHaveTextAsync(names);
        MatchGolden("wardrobes-4-materials", "Lonira", await ReadFolderAsync(page, names));
        (await PicksAsync(page)).Should().ContainSingle().Which.Should().Be(new Pick(true, "readwrite", null));

        // A reload keeps the last folder (IndexedDB); the same order saved again gets new names.
        await page.ReloadAsync();
        await GenerateAsGoldenAsync(page, "wardrobes-4-materials");
        await SaveButton(page).ClickAsync();

        await Expect(Confirmation(page).GetByRole(AriaRole.Listitem)).ToHaveTextAsync(
            names.Select(n => n.Replace(".xlsx", " (2).xlsx", StringComparison.Ordinal)));
        (await PicksAsync(page)).Should().ContainSingle().Which.Should().Be(new Pick(true, "readwrite", true));
        (await ReadFolderAsync(page, names)).Should().HaveCount(4, "the first save's files are still there");
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelling_the_picker_does_nothing_and_a_blocked_picker_points_to_the_downloads()
    {
        await using var context = await app.NewContextAsync();
        await context.AddInitScriptAsync(PickerStub);
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");
        await GenerateAsGoldenAsync(page, "wardrobes-4-materials");

        await page.EvaluateAsync("() => window.pickerError = 'AbortError'");
        await SaveButton(page).ClickAsync();

        await page.WaitForFunctionAsync("() => window.folderPicks.length === 1");
        await Expect(SaveButton(page)).ToBeEnabledAsync();
        await Expect(Confirmation(page)).ToHaveCountAsync(0);
        await Expect(page.Locator(".mud-snackbar")).ToHaveCountAsync(0);

        await page.EvaluateAsync("() => window.pickerError = 'SecurityError'");
        await SaveButton(page).ClickAsync();

        await Expect(page.Locator(".mud-snackbar")).ToContainTextAsync(
            "Браузърът не позволява запис в папка. Изтеглете файловете с „Изтегли всички“.");
        await Expect(Confirmation(page)).ToHaveCountAsync(0);
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task A_browser_without_the_folder_picker_offers_only_the_downloads()
    {
        await using var context = await app.NewContextAsync();
        await context.AddInitScriptAsync("window.showDirectoryPicker = undefined;");
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        await GenerateAsGoldenAsync(page, "wardrobes-4-materials");

        await Expect(DownloadAllButton(page)).ToHaveClassAsync(Filled);
        await Expect(SaveButton(page)).ToHaveCountAsync(0);
        console.Should().BeEmpty();
    }

    [Fact]
    public async Task Where_the_picker_exists_saving_to_a_folder_is_the_primary_action_and_downloading_all_stays()
    {
        await using var context = await app.NewContextAsync();
        await context.AddInitScriptAsync(PickerStub);
        var page = await context.NewPageAsync();
        var console = ConsoleErrors(page);
        await page.GotoAsync("/");

        await GenerateAsGoldenAsync(page, "wardrobes-4-materials");

        await Expect(SaveButton(page)).ToHaveClassAsync(Filled);
        await Expect(DownloadAllButton(page)).ToHaveClassAsync(Outlined);
        await Expect(DownloadAllButton(page)).ToBeEnabledAsync();
        console.Should().BeEmpty();
    }

    // How MudBlazor marks the primary (filled) and the secondary (outlined) button.
    private static readonly Regex Filled = new(@"\bmud-button-filled\b");
    private static readonly Regex Outlined = new(@"\bmud-button-outlined\b");

    private static ILocator DownloadAllButton(IPage page) => page.GetByRole(AriaRole.Button, new() { Name = "Изтегли всички" });

    private static ILocator SaveButton(IPage page) => page.GetByRole(AriaRole.Button, new() { Name = "Запази в папка…" });

    private static ILocator Confirmation(IPage page) => page.GetByTestId("folder-save");

    // Upload → the golden contacts → "Генерирай бланки за поръчка", until the files are listed.
    private static async Task GenerateAsGoldenAsync(IPage page, string fixture)
    {
        await UploadAsync(page, fixture);
        await FillContactsAsync(page, TestData.GoldenContact.CompanyName!, TestData.GoldenContact.MobileNumber!);
        await GenerateButton(page).ClickAsync();
        await Expect(DownloadAllButton(page)).ToBeVisibleAsync();
    }

    private sealed record Pick(bool Activation, string? Mode, bool? StartInIsLastFolder);

    private static async Task<List<Pick>> PicksAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>("() => JSON.stringify(window.folderPicks)");
        return JsonSerializer.Deserialize<List<Pick>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    // The files of the stub's folder with these names, in this order, as saved.
    private static async Task<IReadOnlyList<FileSaveContext>> ReadFolderAsync(IPage page, IEnumerable<string> names)
    {
        var contents = await page.EvaluateAsync<string[]>(
            $$"""
            async names => {
                const folder = await (await navigator.storage.getDirectory()).getDirectoryHandle('{{FolderName}}');
                const contents = [];
                for (const name of names) {
                    const bytes = new Uint8Array(await (await (await folder.getFileHandle(name)).getFile()).arrayBuffer());
                    let binary = '';
                    for (const b of bytes) binary += String.fromCharCode(b);
                    contents.push(btoa(binary));
                }
                return contents;
            }
            """,
            names.ToArray());
        return names.Zip(contents, (name, content) => new FileSaveContext(name, Convert.FromBase64String(content))).ToList();
    }
}
