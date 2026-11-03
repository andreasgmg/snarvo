// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Windows.Foundation;

namespace Snarvo.Services;

/// <summary>A step-by-step guide: a title, an optional introduction and numbered steps with a screenshot each.</summary>
public sealed record Guide(string Title, string Intro, IReadOnlyList<GuideStep> Steps);

public sealed record GuideStep(string ImagePath, string Text);

/// <summary>Writes guides as PDF and Word documents, using nothing beyond the framework and Win2D.</summary>
public static class GuideExport
{
    // Wide enough for a sharp full-width image on paper, small enough to keep the files light.
    const int MaxImageWidth = 1600;

    static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    sealed record Picture(int Width, int Height, byte[] Bgra, byte[] Png);

    static string Today => DateTime.Now.ToString("d MMMM yyyy", Swedish);

    /// <summary>Loads each step's image, shrunk if very wide and flattened onto white like on paper.</summary>
    static async Task<List<Picture>> LoadPicturesAsync(Guide guide, bool png)
    {
        var device = CanvasDevice.GetSharedDevice();
        var pictures = new List<Picture>();
        foreach (var step in guide.Steps)
        {
            var bytes = await File.ReadAllBytesAsync(step.ImagePath);
            using var input = new MemoryStream(bytes).AsRandomAccessStream();
            using var bitmap = await CanvasBitmap.LoadAsync(device, input, 96);

            float scale = Math.Min(1f, (float)MaxImageWidth / bitmap.SizeInPixels.Width);
            int width = Math.Max(1, (int)Math.Round(bitmap.SizeInPixels.Width * scale));
            int height = Math.Max(1, (int)Math.Round(bitmap.SizeInPixels.Height * scale));
            using var target = new CanvasRenderTarget(device, width, height, 96);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Colors.White);
                ds.DrawImage(bitmap, new Rect(0, 0, width, height), bitmap.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
            }

            byte[] encoded = [];
            if (png)
            {
                using var output = new MemoryStream();
                await target.SaveAsync(output.AsRandomAccessStream(), CanvasBitmapFileFormat.Png);
                encoded = output.ToArray();
            }
            pictures.Add(new Picture(width, height, target.GetPixelBytes(), encoded));
        }
        return pictures;
    }

    // ───────────────────────────── PDF ─────────────────────────────

    // A4 in points, with 2 cm margins and room for the page number at the bottom.
    const float PageWidth = 595.28f, PageHeight = 841.89f, Margin = 56.7f;
    const float ContentWidth = PageWidth - 2 * Margin, ContentBottom = PageHeight - Margin - 18;

    public static async Task SavePdfAsync(Guide guide, string path)
    {
        var pictures = await LoadPicturesAsync(guide, png: false);
        var layout = new PdfLayout();

        foreach (var line in Wrap(guide.Title, 22, bold: true, ContentWidth)) layout.Text(line, 22, bold: true, Pdf.Black, lineHeight: 27);
        layout.Gap(2);
        layout.Text(Today, 10, bold: false, Pdf.Grey, lineHeight: 14);
        layout.Gap(10);
        if (!string.IsNullOrWhiteSpace(guide.Intro))
        {
            foreach (var line in Wrap(guide.Intro.Trim(), 11, bold: false, ContentWidth)) layout.Text(line, 11, bold: false, Pdf.Black, lineHeight: 15);
            layout.Gap(8);
        }

        for (int i = 0; i < guide.Steps.Count; i++)
        {
            var text = guide.Steps[i].Text.Trim();
            var lines = text.Length == 0 ? [] : Wrap(text, 11, bold: false, ContentWidth);
            float textHeight = 22 + lines.Count * 15 + 6;

            // Screenshots are drawn at their on-screen size (96 dpi), shrunk to fit the page.
            var picture = pictures[i];
            float width = Math.Min(ContentWidth, picture.Width * 0.75f);
            float height = width * picture.Height / picture.Width;
            float maxHeight = ContentBottom - Margin - textHeight;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * picture.Width / picture.Height;
            }

            // Keep a step's heading, text and image together on one page.
            layout.EnsureRoom(textHeight + height);
            layout.Gap(4);
            layout.Text($"Steg {i + 1}", 14, bold: true, Pdf.Accent, lineHeight: 18);
            foreach (var line in lines) layout.Text(line, 11, bold: false, Pdf.Black, lineHeight: 15);
            layout.Gap(6);
            layout.Image(i, width, height);
            layout.Gap(20);
        }

        await File.WriteAllBytesAsync(path, Pdf.Write(guide.Title, layout.Finish(), pictures));
    }

    /// <summary>Places text and images top to bottom, starting a new page when one is full.</summary>
    sealed class PdfLayout
    {
        readonly List<StringBuilder> _pages = [new()];
        float _y = Margin;

        StringBuilder Page => _pages[^1];

        public void Gap(float height) => _y += height;

        public void EnsureRoom(float height)
        {
            if (_y + height > ContentBottom && _y > Margin + 1) NewPage();
        }

        void NewPage()
        {
            _pages.Add(new());
            _y = Margin;
        }

        public void Text(string line, float size, bool bold, string color, float lineHeight)
        {
            if (_y + lineHeight > ContentBottom) NewPage();
            Pdf.Text(Page, line, Margin, _y + size * 0.85f, size, bold, color);
            _y += lineHeight;
        }

        public void Image(int index, float width, float height)
        {
            if (_y + height > ContentBottom) NewPage();
            float bottom = PageHeight - _y - height;
            Page.Append(CultureInfo.InvariantCulture, $"q {width:0.##} 0 0 {height:0.##} {Margin:0.##} {bottom:0.##} cm /Im{index} Do Q\n");
            // A hairline frame, so screenshots with a white background do not melt into the paper.
            Page.Append(CultureInfo.InvariantCulture, $"0.8 G 0.6 w {Margin:0.##} {bottom:0.##} {width:0.##} {height:0.##} re S\n");
            _y += height;
        }

        public List<StringBuilder> Finish()
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                if (_pages.Count == 1) break;
                var label = $"Sida {i + 1} av {_pages.Count}";
                float x = (PageWidth - Measure(label, 9, bold: false)) / 2;
                Pdf.Text(_pages[i], label, x, PageHeight - Margin + 6, 9, bold: false, Pdf.Grey);
            }
            return _pages;
        }
    }

    /// <summary>Minimal PDF 1.4 output: the standard Helvetica fonts and Flate-compressed RGB images.</summary>
    static class Pdf
    {
        public const string Black = "0.1 0.1 0.1 rg", Grey = "0.45 0.45 0.45 rg", Accent = "0 0.47 0.83 rg";

        /// <summary>Draws one line of text; <paramref name="baseline"/> is measured from the top of the page.</summary>
        public static void Text(StringBuilder page, string text, float x, float baseline, float size, bool bold, string color) =>
            page.Append(CultureInfo.InvariantCulture,
                $"BT {color} /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {PageHeight - baseline:0.##} Td ({Escape(text)}) Tj ET\n");

        // The standard fonts use WinAnsi (Windows-1252), which covers Swedish and most Western text.
        static string Escape(string text)
        {
            var sb = new StringBuilder();
            foreach (byte b in WinAnsi.GetBytes(text))
            {
                if (b is (byte)'\\' or (byte)'(' or (byte)')') sb.Append('\\');
                sb.Append(b < 32 ? ' ' : (char)b);
            }
            return sb.ToString();
        }

        public static byte[] Write(string title, List<StringBuilder> pages, List<Picture> pictures)
        {
            // Object numbers: 1 catalog, 2 page tree, 3–4 fonts, 5 info, then images, then content + page per page.
            int firstImage = 6, firstPage = firstImage + pictures.Count;
            int objectCount = firstPage + pages.Count * 2 - 1;
            var offsets = new long[objectCount + 1];
            using var output = new MemoryStream();

            void Raw(string s)
            {
                var bytes = Encoding.Latin1.GetBytes(s);
                output.Write(bytes);
            }
            void Object(int number, string body)
            {
                offsets[number] = output.Position;
                Raw($"{number} 0 obj\n{body}\nendobj\n");
            }
            void StreamObject(int number, string dictionary, byte[] data)
            {
                offsets[number] = output.Position;
                Raw($"{number} 0 obj\n<< {dictionary} /Length {data.Length} >>\nstream\n");
                output.Write(data);
                Raw("\nendstream\nendobj\n");
            }

            Raw("%PDF-1.4\n%âãÏÓ\n");
            Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
            var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{firstPage + i * 2 + 1} 0 R"));
            Object(2, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");
            Object(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            Object(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            var titleHex = Convert.ToHexString([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(title)]);
            Object(5, $"<< /Title <{titleHex}> /Producer (Snarvo) >>");

            for (int i = 0; i < pictures.Count; i++)
            {
                var p = pictures[i];
                StreamObject(firstImage + i,
                    $"/Type /XObject /Subtype /Image /Width {p.Width} /Height {p.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 " +
                    $"/Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {p.Width} >>",
                    CompressImage(p));
            }

            var images = string.Join(" ", Enumerable.Range(0, pictures.Count).Select(i => $"/Im{i} {firstImage + i} 0 R"));
            for (int i = 0; i < pages.Count; i++)
            {
                int content = firstPage + i * 2, page = content + 1;
                StreamObject(content, "", Encoding.Latin1.GetBytes(pages[i].ToString()));
                Object(page,
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth.ToString(CultureInfo.InvariantCulture)} {PageHeight.ToString(CultureInfo.InvariantCulture)}] " +
                    $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << {images} >> >> /Contents {content} 0 R >>");
            }

            long xref = output.Position;
            var table = new StringBuilder($"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
            for (int i = 1; i <= objectCount; i++) table.Append($"{offsets[i]:D10} 00000 n \n");
            Raw(table.ToString());
            Raw($"trailer\n<< /Size {objectCount + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return output.ToArray();
        }

        /// <summary>RGB rows with a PNG filter per row (the cheapest of None/Sub/Up), then zlib.</summary>
        static byte[] CompressImage(Picture p)
        {
            int stride = p.Width * 3;
            var raw = new byte[stride];
            var previous = new byte[stride];
            var candidates = new byte[3][];
            for (int k = 0; k < 3; k++) candidates[k] = new byte[stride];

            using var output = new MemoryStream();
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (int y = 0; y < p.Height; y++)
                {
                    int row = y * p.Width * 4;
                    for (int x = 0; x < p.Width; x++)
                    {
                        raw[x * 3] = p.Bgra[row + x * 4 + 2];
                        raw[x * 3 + 1] = p.Bgra[row + x * 4 + 1];
                        raw[x * 3 + 2] = p.Bgra[row + x * 4];
                    }

                    int best = 0;
                    long bestCost = long.MaxValue;
                    for (int filter = 0; filter < 3; filter++)
                    {
                        var c = candidates[filter];
                        long cost = 0;
                        for (int i = 0; i < stride; i++)
                        {
                            int predictor = filter switch { 1 => i >= 3 ? raw[i - 3] : 0, 2 => previous[i], _ => 0 };
                            c[i] = (byte)(raw[i] - predictor);
                            int signed = (sbyte)c[i];
                            cost += signed < 0 ? -signed : signed;
                        }
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            best = filter;
                        }
                    }
                    zlib.WriteByte((byte)best);
                    zlib.Write(candidates[best]);
                    Array.Copy(raw, previous, stride);
                }
            }
            return output.ToArray();
        }
    }

    // ───────────────────────────── text measuring ─────────────────────────────

    static readonly Encoding WinAnsi = CreateWinAnsi();

    static Encoding CreateWinAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
    }

    // Advance widths (per 1000 em) of Helvetica and Helvetica-Bold for ASCII 32–126.
    static readonly short[] HelveticaWidths =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    static readonly short[] HelveticaBoldWidths =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    static float CharWidth(char c, bool bold)
    {
        var table = bold ? HelveticaBoldWidths : HelveticaWidths;
        if (c is >= ' ' and <= '~') return table[c - ' '];
        // Accented letters are as wide as their base letter (å → a); other symbols get an average width.
        var decomposed = c.ToString().Normalize(NormalizationForm.FormD);
        if (decomposed.Length > 0 && decomposed[0] is >= ' ' and <= '~' && decomposed[0] != c) return table[decomposed[0] - ' '];
        return c is '—' ? 1000 : 556;
    }

    static float Measure(string text, float size, bool bold) => text.Sum(c => CharWidth(c, bold)) * size / 1000;

    /// <summary>Breaks text into lines that fit the width; explicit line breaks are kept.</summary>
    static List<string> Wrap(string text, float size, bool bold, float width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Measure(candidate, size, bold) <= width)
                {
                    line = candidate;
                    continue;
                }
                if (line.Length > 0) lines.Add(line);
                line = word;
                // A single word wider than the page (a long URL) is broken anywhere.
                while (Measure(line, size, bold) > width && line.Length > 1)
                {
                    int fit = 1;
                    while (fit < line.Length && Measure(line[..(fit + 1)], size, bold) <= width) fit++;
                    lines.Add(line[..fit]);
                    line = line[fit..];
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    // ───────────────────────────── Word ─────────────────────────────

    // A4 with 2 cm margins, in twentieths of a point; images are sized in EMU (1 twip = 635 EMU).
    const int PageWidthTwips = 11906, PageHeightTwips = 16838, MarginTwips = 1134;
    const long MaxImageWidthEmu = (PageWidthTwips - 2 * MarginTwips) * 635L;
    const long MaxImageHeightEmu = (PageHeightTwips - 2 * MarginTwips - 2200) * 635L;

    public static async Task SaveDocxAsync(Guide guide, string path)
    {
        var pictures = await LoadPicturesAsync(guide, png: true);

        var body = new StringBuilder();
        body.Append(Paragraph(guide.Title, "Title"));
        body.Append(Paragraph(Today, "Subtitle"));
        if (!string.IsNullOrWhiteSpace(guide.Intro))
            foreach (var line in Lines(guide.Intro.Trim())) body.Append(Paragraph(line, null));

        for (int i = 0; i < guide.Steps.Count; i++)
        {
            body.Append(Paragraph($"Steg {i + 1}", "Heading1"));
            var text = guide.Steps[i].Text.Trim();
            if (text.Length > 0)
                foreach (var line in Lines(text)) body.Append(Paragraph(line, null, keepWithNext: true));

            var p = pictures[i];
            long cx = Math.Min(MaxImageWidthEmu, p.Width * 9525L);
            long cy = cx * p.Height / p.Width;
            if (cy > MaxImageHeightEmu)
            {
                cy = MaxImageHeightEmu;
                cx = cy * p.Width / p.Height;
            }
            body.Append(ImageParagraph(i + 1, cx, cy));
        }

        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        Add("[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
            <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
            <Default Extension="xml" ContentType="application/xml"/>
            <Default Extension="png" ContentType="image/png"/>
            <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
            <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
            </Types>
            """);
        Add("_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
            </Relationships>
            """);
        Add("docProps/core.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:title>{Xml(guide.Title)}</dc:title>
            </cp:coreProperties>
            """);

        var relationships = new StringBuilder("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            <Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>

            """);
        for (int i = 1; i <= pictures.Count; i++)
            relationships.Append($"""<Relationship Id="rIdImg{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/steg{i}.png"/>""").Append('\n');
        relationships.Append("</Relationships>");
        Add("word/_rels/document.xml.rels", relationships.ToString());

        Add("word/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
            <w:docDefaults>
            <w:rPrDefault><w:rPr><w:rFonts w:ascii="Segoe UI" w:hAnsi="Segoe UI" w:cs="Segoe UI"/><w:sz w:val="22"/><w:lang w:val="sv-SE"/></w:rPr></w:rPrDefault>
            <w:pPrDefault><w:pPr><w:spacing w:after="120" w:line="276" w:lineRule="auto"/></w:pPr></w:pPrDefault>
            </w:docDefaults>
            <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/></w:style>
            <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
            <w:pPr><w:spacing w:after="60"/></w:pPr><w:rPr><w:b/><w:sz w:val="44"/></w:rPr></w:style>
            <w:style w:type="paragraph" w:styleId="Subtitle"><w:name w:val="Subtitle"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
            <w:pPr><w:spacing w:after="240"/></w:pPr><w:rPr><w:color w:val="737373"/><w:sz w:val="20"/></w:rPr></w:style>
            <w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:qFormat/>
            <w:pPr><w:keepNext/><w:spacing w:before="360" w:after="80"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:color w:val="0078D4"/><w:sz w:val="28"/></w:rPr></w:style>
            </w:styles>
            """);

        Add("word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                        xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                        xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">
            <w:body>
            {body}<w:sectPr><w:pgSz w:w="{PageWidthTwips}" w:h="{PageHeightTwips}"/><w:pgMar w:top="{MarginTwips}" w:right="{MarginTwips}" w:bottom="{MarginTwips}" w:left="{MarginTwips}" w:header="567" w:footer="567" w:gutter="0"/></w:sectPr>
            </w:body>
            </w:document>
            """);

        for (int i = 0; i < pictures.Count; i++)
        {
            await using var stream = zip.CreateEntry($"word/media/steg{i + 1}.png", CompressionLevel.NoCompression).Open();
            await stream.WriteAsync(pictures[i].Png);
        }
    }

    static IEnumerable<string> Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    static string Paragraph(string text, string? style, bool keepWithNext = false)
    {
        var properties = (style is null ? "" : $"""<w:pStyle w:val="{style}"/>""") + (keepWithNext ? "<w:keepNext/>" : "");
        return $"""<w:p><w:pPr>{properties}</w:pPr><w:r><w:t xml:space="preserve">{Xml(text)}</w:t></w:r></w:p>""" + "\n";
    }

    static string ImageParagraph(int n, long cx, long cy) =>
        $"""<w:p><w:pPr><w:spacing w:after="240"/></w:pPr><w:r><w:drawing><wp:inline distT="0" distB="0" distL="0" distR="0">""" +
        $"""<wp:extent cx="{cx}" cy="{cy}"/><wp:docPr id="{n}" name="Steg {n}"/>""" +
        """<a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">""" +
        """<pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">""" +
        $"""<pic:nvPicPr><pic:cNvPr id="{n}" name="steg{n}.png"/><pic:cNvPicPr/></pic:nvPicPr>""" +
        $"""<pic:blipFill><a:blip r:embed="rIdImg{n}"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>""" +
        $"""<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="{cx}" cy="{cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom>""" +
        """<a:ln w="9525"><a:solidFill><a:srgbClr val="CCCCCC"/></a:solidFill></a:ln></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>""" + "\n";

    /// <summary>Escapes text for XML and drops control characters XML does not allow.</summary>
    static string Xml(string text) =>
        SecurityElement.Escape(new string(text.Where(c => c is '\t' or '\n' or '\r' || c >= ' ').ToArray())) ?? "";
}
