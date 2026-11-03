// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Snarvo.Services;
using Windows.UI;

namespace Snarvo.Editor;

/// <summary>Border, rounded corners and drop shadow around the whole image.</summary>
public sealed class FrameEffects
{
    public float BorderWidth { get; set; }
    public Color BorderColor { get; set; } = Colors.White;
    public float CornerRadius { get; set; }
    public bool Shadow { get; set; }

    [JsonIgnore] public bool IsEmpty => BorderWidth <= 0 && CornerRadius <= 0 && !Shadow;

    public FrameEffects Clone() => (FrameEffects)MemberwiseClone();
}

/// <summary>An immutable snapshot used for undo / redo.</summary>
public sealed record DocState(CanvasBitmap Base, List<Annotation> Annotations, FrameEffects Effects);

public sealed class SnipDocument
{
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };

    readonly CanvasBitmap _original;

    SnipDocument(string id, CanvasBitmap original)
    {
        Id = id;
        _original = original;
        Base = original;
    }

    public string Id { get; }

    /// <summary>The capture after any crop / cut-out. Annotations are drawn on top.</summary>
    public CanvasBitmap Base { get; set; }

    public List<Annotation> Annotations { get; private set; } = [];
    public FrameEffects Effects { get; set; } = new();

    /// <summary>True for a capture that has never been edited and saved.</summary>
    public bool IsNew { get; private set; } = true;

    public Vector2 Size => new((float)Base.Size.Width, (float)Base.Size.Height);

    public static async Task<SnipDocument> LoadAsync(string id)
    {
        var original = await LoadBitmapAsync(Library.OriginalPath(id));
        var document = new SnipDocument(id, original);

        if (File.Exists(Library.BasePath(id)))
            document.Base = await LoadBitmapAsync(Library.BasePath(id));

        if (File.Exists(Library.DocumentPath(id)))
        {
            document.IsNew = false;
            try
            {
                var file = JsonSerializer.Deserialize<SnipFile>(await File.ReadAllTextAsync(Library.DocumentPath(id)), Json);
                document.Annotations = file?.Annotations ?? [];
                document.Effects = file?.Effects ?? new();
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
        }
        return document;
    }

    /// <summary>
    /// Loads via memory: loading from a path keeps the file locked for as long as the
    /// bitmap lives, which blocks deleting the capture from the library.
    /// </summary>
    static async Task<CanvasBitmap> LoadBitmapAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var stream = new MemoryStream(bytes).AsRandomAccessStream();
        return await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), stream, 96);
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(Library.Root);

        if (ReferenceEquals(Base, _original))
        {
            if (File.Exists(Library.BasePath(Id))) File.Delete(Library.BasePath(Id));
        }
        else
        {
            await Base.SaveAsync(Library.BasePath(Id), CanvasBitmapFileFormat.Png);
        }

        var file = new SnipFile { Annotations = Annotations, Effects = Effects };
        await File.WriteAllTextAsync(Library.DocumentPath(Id), JsonSerializer.Serialize(file, Json));

        using var output = Renderer.RenderFinal(this);
        await output.SaveAsync(Library.OutputPath(Id), CanvasBitmapFileFormat.Png);

        Library.NotifyChanged();
    }

    public DocState Capture() => new(Base, Annotations.Select(a => a.Clone()).ToList(), Effects.Clone());

    public void Restore(DocState state)
    {
        Base = state.Base;
        Annotations = state.Annotations.Select(a => a.Clone()).ToList();
        Effects = state.Effects.Clone();
    }

    sealed class SnipFile
    {
        public List<Annotation>? Annotations { get; set; }
        public FrameEffects? Effects { get; set; }
    }
}
