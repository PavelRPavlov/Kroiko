using FluentAssertions;
using Xunit;

namespace Kroiko.Domain.Tests;

/// <summary>
/// <see cref="FileNameSanitizer"/> turns an order file's name into one a folder accepts and a browser download
/// keeps as it is (ADR-0003 §7). Only the PWA's save layer calls it.
/// </summary>
public sealed class FileNameSanitizerTests
{
    [Theory]
    [InlineData(@"a\b/c:d*e?f""g<h>i|j.xlsx", "a_b_c_d_e_f_g_h_i_j.xlsx")]
    [InlineData("2026-09-24_Мебели 1/2 ООД.xlsx", "2026-09-24_Мебели 1_2 ООД.xlsx")]
    public void Replaces_each_illegal_character_with_an_underscore(string name, string expected)
    {
        FileNameSanitizer.Sanitize(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("Egger\tW1000.xlsx", "Egger_W1000.xlsx")]
    [InlineData("Egger\r\nW1000.xlsx", "Egger__W1000.xlsx")]
    [InlineData("a\u0000b\u001Fc\u007Fd\u0085e.xlsx", "a_b_c_d_e.xlsx")]
    public void Replaces_each_control_character_with_an_underscore(string name, string expected)
    {
        FileNameSanitizer.Sanitize(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("Egger W1000.xlsx.", "Egger W1000.xlsx")]
    [InlineData("Egger W1000.xlsx . . ", "Egger W1000.xlsx")]
    [InlineData("Мебели ООД.", "Мебели ООД")]
    [InlineData("Мебели ООД?", "Мебели ООД_")]
    [InlineData("  Egger.xlsx", "  Egger.xlsx")]
    public void Trims_trailing_dots_and_spaces_only(string name, string expected)
    {
        FileNameSanitizer.Sanitize(name).Should().Be(expected);
    }

    // Windows reserves these device names in any case, with or without an extension (CON.xlsx is CON).
    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("CON.xlsx", "_CON.xlsx")]
    [InlineData("prn.cut_mt", "_prn.cut_mt")]
    [InlineData("Aux.xlsx", "_Aux.xlsx")]
    [InlineData("NUL.tar.xlsx", "_NUL.tar.xlsx")]
    [InlineData("NUL .xlsx", "_NUL .xlsx")]
    [InlineData("COM1.xlsx", "_COM1.xlsx")]
    [InlineData("COM9.xlsx", "_COM9.xlsx")]
    [InlineData("LPT1.xlsx", "_LPT1.xlsx")]
    [InlineData("lpt9.xlsx", "_lpt9.xlsx")]
    [InlineData("CON.", "_CON")]
    public void Prefixes_a_reserved_Windows_name_with_an_underscore(string name, string expected)
    {
        FileNameSanitizer.Sanitize(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("CONSOLE.xlsx")]
    [InlineData("COM0.xlsx")]
    [InlineData("COM10.xlsx")]
    [InlineData("LPT.xlsx")]
    [InlineData("Egger CON.xlsx")]
    [InlineData("КОН.xlsx")]
    public void Leaves_a_name_that_only_resembles_a_reserved_one(string name)
    {
        FileNameSanitizer.Sanitize(name).Should().Be(name);
    }

    // MegaTrading's .cut_mt is named "{CompanyName}.cut_mt", so an empty company name leaves only the extension.
    [Theory]
    [InlineData(".cut_mt", "поръчка.cut_mt")]
    [InlineData(" .xlsx", "поръчка.xlsx")]
    [InlineData(". ..xlsx", "поръчка.xlsx")]
    [InlineData("", "поръчка")]
    [InlineData("   ", "поръчка")]
    [InlineData(". . .", "поръчка")]
    public void Falls_back_to_porachka_keeping_the_extension_when_no_name_is_left(string name, string expected)
    {
        FileNameSanitizer.Sanitize(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("*.xlsx", "_.xlsx")]
    [InlineData("Egger: W1000.cut_mt", "Egger_ W1000.cut_mt")]
    [InlineData("Egger W1000.v2.xlsx", "Egger W1000.v2.xlsx")]
    [InlineData("Egger W1000 .xlsx", "Egger W1000 .xlsx")]
    public void Keeps_the_extension(string name, string expected)
    {
        FileNameSanitizer.Sanitize(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("Egger W1000 Бял гланц.xlsx")]
    [InlineData("2026-09-24_Мебели Иванов ЕООД.xlsx")]
    [InlineData("Мебели Иванов ЕООД.cut_mt")]
    [InlineData("ПДЧ 18мм (дъб) №5, #2 & Co.; 50%.xlsx")]
    public void Keeps_a_name_that_is_already_safe_including_Cyrillic(string name)
    {
        FileNameSanitizer.Sanitize(name).Should().Be(name);
    }
}
