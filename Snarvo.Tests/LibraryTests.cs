// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Globalization;
using System.Runtime.CompilerServices;
using Snarvo.Editor;
using Snarvo.Services;
using Xunit;

namespace Snarvo.Tests;

static class TestData
{
    /// <summary>Points Snarvo at a fresh folder before anything reads its data folder.</summary>
    [ModuleInitializer]
    internal static void UseScratchFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "snarvo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("SNARVO_DATA", folder);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        };
    }
}

/// <summary>Private mode and automatic clean-up: what is deleted, and what must never be.</summary>
[Collection("Library")]
public class LibraryTests
{
    public LibraryTests()
    {
        Assert.StartsWith(Path.GetTempPath(), AppSettings.DataFolder);
        if (Directory.Exists(Library.Root)) Directory.Delete(Library.Root, recursive: true);
        Directory.CreateDirectory(Library.Root);
    }

    static string Capture(DateTime taken, bool temporary = false)
    {
        var id = taken.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        if (temporary) Library.MarkTemporary(id);
        foreach (var path in new[] { Library.OriginalPath(id), Library.OutputPath(id), Library.DocumentPath(id), Library.TextPath(id) })
            File.WriteAllText(path, "x");
        return id;
    }

    static bool Exists(string id) => Directory.GetFiles(Library.Root, id + ".*").Length > 0;

    [Fact]
    public void Private_captures_are_hidden_and_deleted_with_every_file()
    {
        var kept = Capture(DateTime.Now.AddMinutes(-5));
        var temporary = Capture(DateTime.Now, temporary: true);

        Assert.Equal([kept], Library.List());
        Assert.Equal(1, Library.DeleteTemporary());
        Assert.False(Exists(temporary));
        Assert.True(Exists(kept));
    }

    [Fact]
    public void Private_capture_open_in_an_editor_is_left_until_it_closes()
    {
        var open = Capture(DateTime.Now, temporary: true);
        Assert.Equal(0, Library.DeleteTemporary(skip: id => id == open));
        Assert.True(Exists(open));
    }

    [Fact]
    public void Keeping_a_private_capture_puts_it_in_the_library()
    {
        var id = Capture(DateTime.Now, temporary: true);
        Library.Keep(id);
        Assert.Contains(id, Library.List());
        Assert.Equal(0, Library.DeleteTemporary());
    }

    [Fact]
    public void Clean_up_deletes_only_captures_older_than_the_limit()
    {
        var old = Capture(DateTime.Now.AddDays(-8));
        var recent = Capture(DateTime.Now.AddDays(-6));
        var open = Capture(DateTime.Now.AddDays(-9));

        Assert.Equal(2, Library.CountOlderThan(7));
        Assert.Equal(1, Library.DeleteOlderThan(7, skip: id => id == open));
        Assert.False(Exists(old));
        Assert.True(Exists(recent));
        Assert.True(Exists(open));
    }

    [Fact]
    public void Files_that_are_not_captures_are_never_touched()
    {
        var other = Path.Combine(Library.Root, "anteckningar.txt");
        File.WriteAllText(other, "mina egna anteckningar");
        Capture(DateTime.Now.AddDays(-40));
        Library.DeleteOlderThan(1);
        Library.DeleteTemporary();
        Assert.True(File.Exists(other));
    }
}

public class StyleTests
{
    [Fact]
    public void Old_default_colours_move_to_the_new_palette_but_own_choices_stay()
    {
        var saved = new Dictionary<Tool, ToolStyle>
        {
            [Tool.Arrow] = new(Windows.UI.Color.FromArgb(255, 0xE5, 0x39, 0x35), 5, 24),
            [Tool.Rectangle] = new(Windows.UI.Color.FromArgb(255, 0x12, 0x34, 0x56), 4, 24),
        };
        var styles = ToolStyle.WithDefaults(saved);
        Assert.Equal(ToolStyle.Red, styles[Tool.Arrow].Color);
        Assert.Equal(Windows.UI.Color.FromArgb(255, 0x12, 0x34, 0x56), styles[Tool.Rectangle].Color);
        Assert.True(styles.ContainsKey(Tool.Redact));
    }
}
