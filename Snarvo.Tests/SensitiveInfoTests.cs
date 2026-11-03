// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using Snarvo.Editor;
using Windows.Foundation;
using Xunit;

namespace Snarvo.Tests;

/// <summary>Finding personal data in recognised text. A miss here means data is shared by mistake.</summary>
public class SensitiveInfoTests
{
    /// <summary>A line of words, each 10 px wide per letter, laid out left to right on row <paramref name="row"/>.</summary>
    static TextLine Line(string text, int row = 0)
    {
        var words = new List<(string, Rect)>();
        double x = 0;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            words.Add((word, new Rect(x, row * 30, word.Length * 10, 20)));
            x += word.Length * 10 + 8;
        }
        return new TextLine(words);
    }

    static IReadOnlyList<SensitiveHit> Find(params string[] lines) =>
        SensitiveInfo.Find(lines.Select((l, i) => Line(l, i)).ToList());

    [Theory]
    [InlineData("19900314-2381")]   // valid date, wrong check digit: still hidden (OCR may misread a digit)
    [InlineData("900314-2381")]
    [InlineData("199003142381")]
    [InlineData("19900374-2381")]   // samordningsnummer: day + 60
    public void Finds_personal_numbers(string number)
    {
        var hits = Find($"Nummer {number}");
        Assert.Contains(hits, h => h.Kind == "personnummer");
    }

    [Theory]
    [InlineData("Order 20261399-1234")]   // month 13
    [InlineData("Faktura 19901340-0001")] // day 40
    public void Leaves_number_like_order_ids_alone(string line)
    {
        Assert.DoesNotContain(Find(line), h => h.Kind == "personnummer");
    }

    [Fact]
    public void Hides_any_number_on_a_line_labelled_personnummer()
    {
        Assert.Contains(Find("Personnummer: 19901340-0001"), h => h.Kind == "personnummer");
    }

    [Theory]
    [InlineData("maja.lindqvist@exempel.se")]
    [InlineData("maja.lindqvist@exempelse")]   // OCR dropped the dot
    [InlineData("maja.lindqvist @ exempel.se")]
    public void Finds_email(string address)
    {
        Assert.Contains(Find($"E-post {address}"), h => h.Kind == "e-post");
    }

    [Theory]
    [InlineData("070-123 45 67")]
    [InlineData("+46 70 123 45 67")]
    [InlineData("08-123 456 78")]
    public void Finds_phone_numbers(string number)
    {
        Assert.Contains(Find($"Telefon {number}"), h => h.Kind == "telefonnummer");
    }

    [Fact]
    public void Finds_card_numbers_with_valid_check_digit_only()
    {
        Assert.Contains(Find("Kort 4111 1111 1111 1111"), h => h.Kind == "kortnummer");
        Assert.DoesNotContain(Find("Kort 4111 1111 1111 1112"), h => h.Kind == "kortnummer");
    }

    [Theory]
    [InlineData("Lösenord: hemligt123")]
    [InlineData("password = Sommar2026!")]
    [InlineData("token ghp_abcdefghijklmnopqrstuvwx1234")]
    public void Finds_passwords_and_keys(string line)
    {
        Assert.NotEmpty(Find(line));
    }

    [Fact]
    public void Hit_covers_the_whole_word_and_only_its_line()
    {
        var hits = Find("Namn Maja", "E-post maja@exempel.se");
        var hit = Assert.Single(hits);
        Assert.Equal(30, hit.Bounds.Y);
        Assert.Equal(6 * 10 + 8, hit.Bounds.X); // after "E-post"
    }

    [Fact]
    public void Ordinary_text_has_no_hits()
    {
        Assert.Empty(Find("Fyll i uppgifterna och spara.", "Medlemskap Ordinarie, 250 kr/år", "Version 1.2.0"));
    }

    [Fact]
    public void Finds_names_ignoring_case_and_lost_dots()
    {
        // OCR often reads "Åström" as "Astrom".
        var hits = SensitiveInfo.FindTerms([Line("Fredrik Astrom, Umeå"), Line("Övrigt", 1)], ["fredrik åström"]);
        Assert.Single(hits);
    }

    [Fact]
    public void Cover_boxes_are_padded_and_not_doubled()
    {
        var hits = Find("Rubrik", "E-post maja@exempel.se"); // second row, so there is room above
        var styles = ToolStyle.CreateDefaults();
        var first = AutoHide.Create(hits, Services.HideMethod.Cover, new(400, 100), [], styles);
        var box = Assert.Single(first).Box;
        Assert.IsType<RedactAnnotation>(box);
        Assert.True(box.Start.Y < hits[0].Bounds.Y && box.End.Y > hits[0].Bounds.Bottom);

        // Running it again on the same picture adds nothing.
        var covered = AutoHide.Covered([box]);
        Assert.Empty(AutoHide.Create(hits, Services.HideMethod.Cover, new(400, 100), covered, styles));
    }
}
