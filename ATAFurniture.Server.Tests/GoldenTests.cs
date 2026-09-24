using System.Collections.ObjectModel;
using System.Globalization;
using ATAFurniture.Server.Models;
using ATAFurniture.Server.TemplateBuilding;
using ATAFurniture.Server.TemplateBuilding.Lonira;
using ATAFurniture.Server.TemplateBuilding.Suliver;
using Kroiko.Domain;
using Kroiko.Domain.CellsExtracting;
using Kroiko.Domain.ExcelFilesGeneration;
using Kroiko.Domain.ExcelFilesGeneration.XlsxWrapper;
using Kroiko.Domain.TemplateBuilding;
using Kroiko.Domain.TemplateBuilding.Lonira;
using Kroiko.Domain.TemplateBuilding.MegaTrading;
using Kroiko.Domain.TemplateBuilding.Suliver;
using Kroiko.Domain.TextFileGeneration;
using Kroiko.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ATAFurniture.Server.Tests;

/// <summary>
/// Characterization tests (01 step 3, ADR-0004 §8): every order file today's Server produces for each
/// valid fixture × manufacturer matches its golden file in <c>Kroiko.Testing/TestData/golden/</c>.
/// Re-record with <c>UPDATE_GOLDEN=1</c> only for an intended output change, and explain the diff in the PR.
/// </summary>
public sealed class GoldenTests
{
    private static readonly string[] ValidFixtures =
    [
        "bathroom-4-materials",
        "beds-5-materials",
        "cabinet-23-field",
        "kitchen-8-materials",
        "wardrobes-4-materials",
    ];

    private static readonly string[] Manufacturers =
    [
        nameof(SupportedCompanies.Lonira),
        nameof(SupportedCompanies.Suliver),
        nameof(SupportedCompanies.MegaTrading),
    ];

    // Fixtures with more than 6 materials (kitchen-8-materials) are recorded for MegaTrading as they are
    // today, with the .cut_mt header truncated to 6 material rows, as characterization (ADR-0007 §7).
    public static TheoryData<string, string> EveryValidFixtureAndManufacturer()
    {
        var data = new TheoryData<string, string>();
        foreach (var fixture in ValidFixtures)
        {
            foreach (var manufacturer in Manufacturers)
            {
                data.Add(fixture, manufacturer);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryValidFixtureAndManufacturer))]
    public async Task Todays_order_files_match_the_golden_files(string fixture, string manufacturer)
    {
        var files = await RunPipelineAsync(fixture, manufacturer);
        OrderFilesAssert.MatchGolden(fixture, manufacturer, files);
    }

    // 01 step 4 (ADR-0004 §8): a bg-BG host must produce the same order files as the invariant recording.
    // Today only the MegaTrading .cut_mt differs, by decimal commas ("609,18"); see CONTEXT.md §7.
    [Theory(Skip = "Un-skipped in phase 02 step 4 — invariant culture")]
    [MemberData(nameof(EveryValidFixtureAndManufacturer))]
    public async Task Order_files_under_bg_BG_match_the_golden_files(string fixture, string manufacturer)
    {
        var files = await RunPipelineAsync(fixture, manufacturer, culture: CultureInfo.GetCultureInfo("bg-BG"));

        // Always compare, even under UPDATE_GOLDEN=1: the golden files are the invariant-culture recording.
        OrderFilesAssert.MatchGolden(fixture, manufacturer, files, TestData.GoldenRoot, update: false);
    }

    [Fact]
    public async Task Suliver_with_a_different_edge_color_matches_the_golden_files()
    {
        // cabinet-23-field has oversized details ("СДВ с краен размер …") and "Different" edges
        // ("Кантиране с друг цвят"); the operator's colour fills the template's {DifferentEdgeColor} cell.
        const string fixture = "cabinet-23-field";
        var files = await RunPipelineAsync(fixture, nameof(SupportedCompanies.Suliver), differentEdgeColor: "Бял гланц");

        OrderFilesAssert.MatchGolden(fixture, "Suliver-different-edge-color", files);
    }

    // The only place that knows how today's Server turns a fixture into order files.
    // Phase 02 changes this method's body — and nothing else in the tests.
    private static async Task<IReadOnlyList<FileSaveContext>> RunPipelineAsync(
        string fixture, string manufacturer, string? differentEdgeColor = null, CultureInfo? culture = null)
    {
        // The host's culture for this run: invariant unless a test says otherwise (the bg-BG test).
        var hostCulture = culture ?? CultureInfo.InvariantCulture;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = hostCulture;
        CultureInfo.CurrentUICulture = hostCulture;
        try
        {
            // FileUploadComponent.razor: parse the uploaded file into ConverterContext.Details.
            using var stream = new MemoryStream(await File.ReadAllBytesAsync(TestData.Polyboard(fixture)));
            var extractor = new DetailsExtractorService(NullLogger<DetailsExtractorService>.Instance);
            var details = new ObservableCollection<Detail>(await extractor.ExtractDetails(stream));
            if (!details.Any())
            {
                // The live app stops here with an error, so there is nothing to record.
                throw new InvalidOperationException($"'{fixture}' parses to no details; it is not a valid fixture.");
            }

            var files = GroupIntoFiles(manufacturer, details);

            // Copied from Startup.ConfigureServices (keep in step with it): the keyed builder, row provider and file-name provider, resolved by
            // company name as OrderHandlingComponent.GenerateFiles does. The builders read their template.json
            // from next to ATAFurniture.Server.dll, as in production.
            var services = new ServiceCollection();
            services.AddKeyedScoped<ITemplateBuilder, LoniraTemplateBuilder>(nameof(SupportedCompanies.Lonira));
            services.AddKeyedScoped<ITableRowProvider, LoniraTableRowProvider>(nameof(SupportedCompanies.Lonira));
            services.AddKeyedScoped<IFileNameProvider, LoniraFileNameProvider>(nameof(SupportedCompanies.Lonira));
            services.AddKeyedScoped<ITemplateBuilder, SuliverTemplateBuilder>(nameof(SupportedCompanies.Suliver));
            services.AddKeyedScoped<ITableRowProvider, SuliverTableRowProvider>(nameof(SupportedCompanies.Suliver));
            services.AddKeyedScoped<IFileNameProvider, SuliverFileNameProvider>(nameof(SupportedCompanies.Suliver));
            services.AddKeyedScoped<ITemplateBuilder, MegaTradingTemplateBuilder>(nameof(SupportedCompanies.MegaTrading));
            services.AddKeyedScoped<ITableRowProvider, MegaTradingTableRowProvider>(nameof(SupportedCompanies.MegaTrading));
            services.AddKeyedScoped<IFileNameProvider, MegaTradingFileNameProvider>(nameof(SupportedCompanies.MegaTrading));
            services.AddScoped<IExcelFileGenerator, ExcelFileGenerator>();
            services.AddScoped<ITextFileGenerator, MegaTradingFileGenerator>();
            services.AddScoped<FileGeneratorService>();
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = provider.CreateAsyncScope();
            var serviceProvider = scope.ServiceProvider;

            // OrderHandlingComponent.GenerateFiles.
            var fileNameProvider = serviceProvider.GetKeyedService<IFileNameProvider>(manufacturer);
            var templateBuilder = serviceProvider.GetKeyedService<ITemplateBuilder>(manufacturer);
            var contact = new ContactInfo
            {
                CompanyName = "Тест ООД",
                MobileNumber = "0888123456",
                Email = "test@example.com",
            };
            return await serviceProvider.GetRequiredService<FileGeneratorService>().CreateFiles(
                contact,
                files,
                templateBuilder,
                fileNameProvider,
                // ConverterContext.DifferentEdgeColor starts as string.Empty when the operator leaves it alone.
                differentEdgeColor ?? string.Empty,
                manufacturer == SupportedCompanies.MegaTrading.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    // Copied verbatim from FileDisplayComponent.razor's LoadDataSource (ATAFurniture.Server/Components/
    // FileDisplay/), with Context.TargetCompany.Name -> manufacturer and Context.Details -> details.
    private static List<KroikoFile> GroupIntoFiles(string manufacturer, ObservableCollection<Detail> details)
    {
        var result = new List<KroikoFile>();
        switch (manufacturer)
        {
            case nameof(SupportedCompanies.Lonira):
                var groups = details.GroupBy(x => x.Material).ToList();
                foreach (var group in groups.Where(group => !string.IsNullOrEmpty(group.Key)))
                {
                    result.Add(new KroikoFile { FileName = group.Key, Details = group.ToList().ToLoniraDetails() });
                }

                break;
            case nameof(SupportedCompanies.Suliver):
                if (details.Any())
                {
                    result.Add(new KroikoFile
                    {
                        // TODO what is the required file name
                        FileName = "Suliver",
                        Details = details.ToSuliverDetails()
                    });
                }
                break;
            case nameof(SupportedCompanies.MegaTrading):
                if (details.Any())
                {
                    result.Add(new KroikoFile
                    {
                        // TODO what is the required file name
                        FileName = "MegaTrading",
                        Details = details.ToMegaTradingDetails()
                    });
                }
                break;
        }

        return result;
    }
}
