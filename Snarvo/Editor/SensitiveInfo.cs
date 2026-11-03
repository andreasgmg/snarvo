// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.Media.Ocr;

namespace Snarvo.Editor;

/// <summary>A piece of recognised text that should not be shared, and where it is in the image.</summary>
public sealed record SensitiveHit(string Kind, string Text, Rect Bounds);

/// <summary>One recognised line: its words and where each is in the image (image coordinates).</summary>
public sealed record TextLine(IReadOnlyList<(string Text, Rect Bounds)> Words);

/// <summary>Finds e-mail addresses, phone numbers, passwords, keys etc. in an OCR result.</summary>
public static partial class SensitiveInfo
{
    // Label followed by ':' or '=' – everything after it on the line is the secret.
    [GeneratedRegex(@"\b(?:lösen(?:ord)?|password|passwd|pwd|pin(?:kod|code)?|token|secret|hemlig(?:het)?|api[\s_-]?(?:nyckel|key)|access[\s_-]?key|nyckel|key)\s*[:=]\s*(?<value>\S.*)", RegexOptions.IgnoreCase)]
    private static partial Regex LabeledSecret();

    // The dot before the top-level domain is optional: OCR often drops it ("exempelse").
    [GeneratedRegex(@"[\w.+-]+\s?@\s?[\w-]{2,}(?:\.[\w-]+)*", RegexOptions.IgnoreCase)]
    private static partial Regex Email();

    // Swedish personnummer / samordningsnummer, with or without century.
    [GeneratedRegex(@"\b(?:19|20)?\d{6}[-+]?\d{4}\b")]
    private static partial Regex PersonalNumber();

    [GeneratedRegex(@"\b(?:person|pers|p)\.?\s?(?:nummer|nr|nr\.)|samordningsnummer", RegexOptions.IgnoreCase)]
    private static partial Regex PersonalNumberLabel();

    [GeneratedRegex(@"\b(?:\d[ -]?){12,18}\d\b")]
    private static partial Regex CardNumber();

    // International (+46 70 …) or national (070-…, 08-…) numbers.
    [GeneratedRegex(@"(?:\+\d{1,3}[\s-]?|\b0)\d{1,4}(?:[\s-]?\d{2,4}){2,4}\b")]
    private static partial Regex Phone();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:\s?[A-Z0-9]{4}){3,7}(?:\s?[A-Z0-9]{1,3})?\b")]
    private static partial Regex Iban();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex IpAddress();

    // Well-known key prefixes (OpenAI, Stripe, GitHub, Slack, AWS, Google, JWT).
    [GeneratedRegex(@"^(?:sk-|pk_|rk_|sk_|ghp_|gho_|ghs_|github_pat_|xox[abposr]-|AKIA|AIza|eyJ)[A-Za-z0-9_\-.]{8,}")]
    private static partial Regex KnownToken();

    // Long random-looking word: letters and digits mixed, no spaces.
    [GeneratedRegex(@"^[A-Za-z0-9_\-]{24,}$")]
    private static partial Regex LongToken();

    /// <summary>The lines of an OCR result, with the word areas converted to image coordinates.</summary>
    public static IReadOnlyList<TextLine> Lines(OcrResult result, float scale) =>
        result.Lines.Select(line => new TextLine(line.Words
            .Select(w => (w.Text, new Rect(w.BoundingRect.X / scale, w.BoundingRect.Y / scale,
                w.BoundingRect.Width / scale, w.BoundingRect.Height / scale)))
            .ToList())).ToList();

    /// <summary>A line as one string, with a way back from a range of it to where those words are in the image.</summary>
    sealed class LineText
    {
        readonly IReadOnlyList<(string Text, Rect Bounds)> _words;
        readonly int[] _starts;

        public LineText(TextLine line)
        {
            _words = line.Words;
            _starts = new int[_words.Count];
            var text = new StringBuilder();
            for (int i = 0; i < _words.Count; i++)
            {
                if (i > 0) text.Append(' ');
                _starts[i] = text.Length;
                text.Append(_words[i].Text);
            }
            Text = text.ToString();
        }

        public string Text { get; }
        public int WordCount => _words.Count;
        public int WordStart(int i) => _starts[i];
        public string Word(int i) => _words[i].Text;

        /// <summary>Image area of every word the range touches, so a partial match hides the whole word.</summary>
        public Rect? Bounds(int index, int length)
        {
            Rect? bounds = null;
            for (int i = 0; i < _words.Count; i++)
            {
                int start = _starts[i], end = start + _words[i].Text.Length;
                if (end <= index || start >= index + length) continue;
                var r = _words[i].Bounds;
                bounds = bounds is { } b ? Union(b, r) : r;
            }
            return bounds;
        }
    }

    static Rect Union(Rect a, Rect b)
    {
        double left = Math.Min(a.Left, b.Left), top = Math.Min(a.Top, b.Top);
        return new Rect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    public static IReadOnlyList<SensitiveHit> Find(OcrResult result, float scale) => Find(Lines(result, scale));

    public static IReadOnlyList<SensitiveHit> Find(IReadOnlyList<TextLine> lines)
    {
        var hits = new List<SensitiveHit>();
        foreach (var textLine in lines)
        {
            if (textLine.Words.Count == 0) continue;
            var line = new LineText(textLine);
            var lineText = line.Text;

            void Add(string kind, int index, int length)
            {
                if (line.Bounds(index, length) is { } bounds)
                    hits.Add(new SensitiveHit(kind, lineText.Substring(index, length), bounds));
            }

            // Most specific patterns first: when finds overlap, the first one names the hit.
            foreach (Match m in LabeledSecret().Matches(lineText))
                Add("lösenord/nyckel", m.Groups["value"].Index, m.Groups["value"].Length);
            foreach (Match m in Email().Matches(lineText))
                Add("e-post", m.Index, m.Length);
            foreach (Match m in Iban().Matches(lineText))
                Add("IBAN", m.Index, m.Length);
            foreach (Match m in CardNumber().Matches(lineText))
                if (PassesLuhn(m.Value)) Add("kortnummer", m.Index, m.Length);
            bool labelled = PersonalNumberLabel().IsMatch(lineText);
            foreach (Match m in PersonalNumber().Matches(lineText))
                if (labelled || IsPersonalNumber(m.Value)) Add("personnummer", m.Index, m.Length);
            foreach (Match m in Phone().Matches(lineText))
                if (m.Value.Count(char.IsDigit) >= 8) Add("telefonnummer", m.Index, m.Length);
            foreach (Match m in IpAddress().Matches(lineText))
                if (m.Value.Split('.').All(part => int.Parse(part) <= 255)) Add("IP-adress", m.Index, m.Length);

            for (int i = 0; i < line.WordCount; i++)
            {
                var word = line.Word(i);
                bool known = KnownToken().IsMatch(word);
                bool random = LongToken().IsMatch(word) && word.Any(char.IsDigit) && word.Any(char.IsLetter);
                if (known || random) Add("nyckel/token", line.WordStart(i), word.Length);
            }
        }
        return Merge(hits);
    }

    /// <summary>
    /// Every place where one of the terms (a word, a name …) occurs. Matching ignores case and
    /// accents, since OCR often drops the dots and rings of å, ä and ö.
    /// </summary>
    public static IReadOnlyList<SensitiveHit> FindTerms(OcrResult result, float scale, IReadOnlyList<string> terms) =>
        FindTerms(Lines(result, scale), terms);

    public static IReadOnlyList<SensitiveHit> FindTerms(IReadOnlyList<TextLine> lines, IReadOnlyList<string> terms)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;
        var hits = new List<SensitiveHit>();
        foreach (var textLine in lines)
        {
            if (textLine.Words.Count == 0) continue;
            var line = new LineText(textLine);
            foreach (var term in terms)
            {
                int from = 0;
                while (from < line.Text.Length)
                {
                    int i = compare.IndexOf(line.Text.AsSpan(from), term, Loose, out int length);
                    if (i < 0) break;
                    if (line.Bounds(from + i, length) is { } bounds) hits.Add(new SensitiveHit(term, term, bounds));
                    from += i + Math.Max(1, length);
                }
            }
        }
        return Merge(hits);
    }

    /// <summary>One hit per area: overlapping finds (e.g. a key that is also "after Lösenord:") are joined.</summary>
    static List<SensitiveHit> Merge(List<SensitiveHit> hits)
    {
        var merged = new List<SensitiveHit>();
        foreach (var hit in hits)
        {
            int i = merged.FindIndex(m => Overlaps(m.Bounds, hit.Bounds));
            if (i < 0) merged.Add(hit);
            else merged[i] = merged[i] with { Bounds = Union(merged[i].Bounds, hit.Bounds) };
        }
        return merged;
    }

    static bool Overlaps(Rect a, Rect b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    /// <summary>
    /// Looks like a Swedish personnummer or samordningsnummer: a real month and a day of 1–31
    /// (61–91 for samordningsnummer). Keeps order numbers and the like visible. The check digit
    /// is deliberately not required: one digit misread by OCR must not leave a real number visible.
    /// </summary>
    internal static bool IsPersonalNumber(string text)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length == 12) digits = digits[2..];
        if (digits.Length != 10) return false;
        int month = int.Parse(digits.AsSpan(2, 2)), day = int.Parse(digits.AsSpan(4, 2));
        if (month is < 1 or > 12) return false;
        if (day > 60) day -= 60;
        return day is >= 1 and <= 31;
    }

    static bool PassesLuhn(string number)
    {
        var digits = number.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length < 13) return false;
        int sum = 0;
        for (int i = 0; i < digits.Length; i++)
        {
            int d = digits[digits.Length - 1 - i];
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
        }
        return sum % 10 == 0;
    }
}
