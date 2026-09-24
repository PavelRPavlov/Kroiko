using FluentAssertions;
using Kroiko.Client.Blazor.Conversion;
using Xunit;

namespace Kroiko.Client.Tests.Conversion;

/// <summary>
/// "Запази в папка…" never overwrites: a name already in the folder, or already taken by an earlier file of the same
/// save, gets <c> (2)</c>, <c> (3)</c>, … before its extension (ADR-0003 §4; docs/implementation/05-saving.md, step 1).
/// </summary>
public sealed class ClashNamingTests
{
    [Fact]
    public void Names_that_clash_with_nothing_are_kept()
    {
        var finalNames = ClashNaming.FinalNames(
            ["Egger W1000.xlsx", "Kronospan K001.xlsx"],
            ["2026-09-23_Мебели ООД.xlsx"]);

        finalNames.Should().Equal("Egger W1000.xlsx", "Kronospan K001.xlsx");
    }

    [Fact]
    public void A_name_already_in_the_folder_gets_2_before_its_extension()
    {
        var finalNames = ClashNaming.FinalNames(
            ["Egger W1000.xlsx", "Kronospan K001.xlsx"],
            ["Egger W1000.xlsx"]);

        finalNames.Should().Equal("Egger W1000 (2).xlsx", "Kronospan K001.xlsx");
    }

    [Fact]
    public void A_run_of_clashes_takes_the_first_free_number()
    {
        var finalNames = ClashNaming.FinalNames(
            ["Egger W1000.xlsx"],
            ["Egger W1000.xlsx", "Egger W1000 (2).xlsx", "Egger W1000 (3).xlsx", "Egger W1000 (5).xlsx"]);

        finalNames.Should().Equal("Egger W1000 (4).xlsx");
    }

    // A Windows folder ignores case, so writing "Egger W1000.xlsx" would overwrite "egger w1000.XLSX".
    [Fact]
    public void A_name_that_differs_only_in_case_clashes()
    {
        var finalNames = ClashNaming.FinalNames(
            ["Egger W1000.xlsx", "ПДЧ Бял.xlsx", "пдч бял.xlsx"],
            ["egger w1000.XLSX", "EGGER W1000 (2).xlsx"]);

        finalNames.Should().Equal("Egger W1000 (3).xlsx", "ПДЧ Бял.xlsx", "пдч бял (2).xlsx");
    }

    // The extension is everything from the last dot, as FileNameSanitizer reads it.
    [Theory]
    [InlineData("поръчка", "поръчка (2)")]
    [InlineData("Egger W1000.v2.xlsx", "Egger W1000.v2 (2).xlsx")]
    [InlineData("Мебели ООД.cut_mt", "Мебели ООД (2).cut_mt")]
    public void The_number_goes_before_the_extension_or_at_the_end_of_a_name_without_one(string name, string expected)
    {
        var finalNames = ClashNaming.FinalNames([name], [name]);

        finalNames.Should().Equal(expected);
    }

    // Two materials whose names differ only in characters the sanitiser replaces ("1/2" and "1:2") end up as one name.
    [Fact]
    public void Two_files_of_one_save_with_the_same_name_do_not_overwrite_each_other()
    {
        var finalNames = ClashNaming.FinalNames(
            ["ПДЧ 1_2.xlsx", "ПДЧ 1_2.xlsx", "ПДЧ 1_2.xlsx"],
            ["ПДЧ 1_2 (2).xlsx"]);

        finalNames.Should().Equal("ПДЧ 1_2.xlsx", "ПДЧ 1_2 (3).xlsx", "ПДЧ 1_2 (4).xlsx");
    }

    // Its number is part of its name, not a clash number; still, no final name is in the folder or given twice.
    [Fact]
    public void A_file_named_like_a_numbered_file_gets_its_own_number()
    {
        var existing = new[] { "Egger.xlsx", "Egger (2) (2).xlsx" };

        var finalNames = ClashNaming.FinalNames(["Egger.xlsx", "Egger (2).xlsx", "Egger.xlsx"], existing);

        finalNames.Should().Equal("Egger (2).xlsx", "Egger (2) (3).xlsx", "Egger (3).xlsx");
        finalNames.Should().OnlyHaveUniqueItems().And.NotIntersectWith(existing);
    }
}
