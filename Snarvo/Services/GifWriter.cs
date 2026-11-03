// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Runtime.InteropServices;

namespace Snarvo.Services;

/// <summary>
/// Minimal animated-GIF encoder tuned for screen recordings: an exact or median-cut palette per
/// frame, only the changed area of each frame is stored, and identical frames are merged.
/// </summary>
public sealed class GifWriter
{
    readonly Stream _out;
    readonly int _width, _height;
    uint[]? _written;   // what the viewer currently shows
    uint[]? _pending;   // the next frame, held back so identical successors can extend its delay
    int _pendingDelay;

    public GifWriter(Stream output, int width, int height)
    {
        _out = output;
        _width = width;
        _height = height;

        WriteAscii("GIF89a");
        WriteUInt16(width);
        WriteUInt16(height);
        _out.WriteByte(0x70); // no global colour table, 8-bit colour resolution
        _out.WriteByte(0);
        _out.WriteByte(0);

        // NETSCAPE2.0 extension: loop forever.
        _out.Write([0x21, 0xFF, 0x0B]);
        WriteAscii("NETSCAPE2.0");
        _out.Write([0x03, 0x01, 0x00, 0x00, 0x00]);
    }

    /// <param name="bgra">Top-down BGRA pixels of the full frame.</param>
    /// <param name="delay">How long to show the frame, in hundredths of a second.</param>
    public void AddFrame(byte[] bgra, int delay)
    {
        var pixels = MemoryMarshal.Cast<byte, uint>(bgra).ToArray();
        for (int i = 0; i < pixels.Length; i++) pixels[i] |= 0xFF000000;

        if (_pending is not null && _pending.AsSpan().SequenceEqual(pixels))
        {
            _pendingDelay += delay;
            return;
        }
        FlushPending();
        _pending = pixels;
        _pendingDelay = delay;
    }

    public void Finish()
    {
        FlushPending();
        _out.WriteByte(0x3B);
        _out.Flush();
    }

    void FlushPending()
    {
        if (_pending is null) return;
        var (x, y, w, h) = ChangedArea(_written, _pending);
        WriteFrame(_pending, x, y, w, h, _pendingDelay);
        _written = _pending;
        _pending = null;
    }

    (int X, int Y, int W, int H) ChangedArea(uint[]? before, uint[] after)
    {
        if (before is null) return (0, 0, _width, _height);
        int minX = _width, minY = _height, maxX = -1, maxY = -1;
        for (int y = 0; y < _height; y++)
        {
            int row = y * _width;
            for (int x = 0; x < _width; x++)
            {
                if (before[row + x] == after[row + x]) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        // Nothing changed (only happens for the timing of a merged frame): store a single pixel.
        return maxX < 0 ? (0, 0, 1, 1) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    void WriteFrame(uint[] pixels, int x, int y, int w, int h, int delay)
    {
        var area = new uint[w * h];
        for (int row = 0; row < h; row++)
            Array.Copy(pixels, (y + row) * _width + x, area, row * w, w);
        var (palette, indices) = Quantize(area);

        // Graphic control extension: keep the previous frame underneath (disposal 1).
        _out.Write([0x21, 0xF9, 0x04, 0x04]);
        WriteUInt16(Math.Clamp(delay, 2, 65535));
        _out.Write([0x00, 0x00]);

        // Image descriptor with a 256-entry local colour table.
        _out.WriteByte(0x2C);
        WriteUInt16(x);
        WriteUInt16(y);
        WriteUInt16(w);
        WriteUInt16(h);
        _out.WriteByte(0x87);
        for (int i = 0; i < 256; i++)
        {
            uint c = i < palette.Count ? palette[i] : 0;
            _out.WriteByte((byte)(c >> 16));
            _out.WriteByte((byte)(c >> 8));
            _out.WriteByte((byte)c);
        }
        WriteLzw(indices);
    }

    // ───────────────────────────── palette ─────────────────────────────

    /// <summary>Exact colours when there are at most 256 of them (typical for UI), else median cut.</summary>
    static (List<uint> Palette, byte[] Indices) Quantize(uint[] pixels)
    {
        var exact = new Dictionary<uint, byte>();
        foreach (var p in pixels)
        {
            if (exact.ContainsKey(p)) continue;
            if (exact.Count == 256) break;
            exact[p] = (byte)exact.Count;
        }
        if (exact.Count < 256 || pixels.All(exact.ContainsKey))
        {
            var palette = exact.OrderBy(kv => kv.Value).Select(kv => kv.Key & 0xFFFFFF).ToList();
            return (palette, pixels.Select(p => exact[p]).ToArray());
        }
        return MedianCut(pixels);
    }

    static int Bucket(uint c) => (int)(((c >> 19) & 0x1F) << 10 | ((c >> 11) & 0x1F) << 5 | ((c >> 3) & 0x1F));

    static (List<uint>, byte[]) MedianCut(uint[] pixels)
    {
        // Histogram over 15-bit colours, keeping full-precision sums for accurate averages.
        var count = new int[32768];
        var sumR = new long[32768];
        var sumG = new long[32768];
        var sumB = new long[32768];
        foreach (var p in pixels)
        {
            int b = Bucket(p);
            count[b]++;
            sumR[b] += (p >> 16) & 0xFF;
            sumG[b] += (p >> 8) & 0xFF;
            sumB[b] += p & 0xFF;
        }
        var keys = Enumerable.Range(0, 32768).Where(b => count[b] > 0).ToArray();

        static int Channel(int key, int ch) => (key >> (10 - 5 * ch)) & 0x1F;

        var boxes = new List<(int Start, int End)> { (0, keys.Length) };
        while (boxes.Count < 256)
        {
            // Split the box spanning the widest colour range: keeps small but distinct colours (cursor, accents).
            int best = -1, bestRange = 0, bestChannel = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                var (start, end) = boxes[i];
                if (end - start < 2) continue;
                for (int ch = 0; ch < 3; ch++)
                {
                    int lo = 31, hi = 0;
                    for (int k = start; k < end; k++)
                    {
                        int v = Channel(keys[k], ch);
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                    if (hi - lo > bestRange)
                    {
                        bestRange = hi - lo;
                        best = i;
                        bestChannel = ch;
                    }
                }
            }
            if (best < 0) break;

            var box = boxes[best];
            int channel = bestChannel;
            Array.Sort(keys, box.Start, box.End - box.Start,
                Comparer<int>.Create((a, b) => Channel(a, channel).CompareTo(Channel(b, channel))));
            long total = 0;
            for (int k = box.Start; k < box.End; k++) total += count[keys[k]];
            long running = 0;
            int split = box.Start + 1;
            for (int k = box.Start; k < box.End - 1; k++)
            {
                running += count[keys[k]];
                split = k + 1;
                if (running * 2 >= total) break;
            }
            boxes[best] = (box.Start, split);
            boxes.Add((split, box.End));
        }

        var palette = new List<uint>();
        var lookup = new byte[32768];
        foreach (var (start, end) in boxes)
        {
            long n = 0, r = 0, g = 0, b = 0;
            for (int k = start; k < end; k++)
            {
                int key = keys[k];
                n += count[key];
                r += sumR[key];
                g += sumG[key];
                b += sumB[key];
                lookup[key] = (byte)palette.Count;
            }
            palette.Add((uint)((r / n) << 16 | (g / n) << 8 | (b / n)));
        }
        return (palette, pixels.Select(p => lookup[Bucket(p)]).ToArray());
    }

    // ───────────────────────────── LZW ─────────────────────────────

    /// <summary>GIF variant of LZW with variable code width, following the classic compress/NGif scheme.</summary>
    void WriteLzw(byte[] indices)
    {
        const int MinCodeSize = 8, Clear = 256, End = 257, MaxCodes = 4096;
        _out.WriteByte(MinCodeSize);

        var block = new byte[255];
        int blockLength = 0;
        int bitBuffer = 0, bitCount = 0;
        int codeSize = MinCodeSize + 1, maxCode = (1 << codeSize) - 1, next = End + 1;
        bool clearing = false;
        var table = new Dictionary<int, int>();

        void FlushBlock()
        {
            if (blockLength == 0) return;
            _out.WriteByte((byte)blockLength);
            _out.Write(block, 0, blockLength);
            blockLength = 0;
        }

        void Emit(int code)
        {
            bitBuffer |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                block[blockLength++] = (byte)bitBuffer;
                if (blockLength == 255) FlushBlock();
                bitBuffer >>= 8;
                bitCount -= 8;
            }
            if (clearing)
            {
                codeSize = MinCodeSize + 1;
                maxCode = (1 << codeSize) - 1;
                clearing = false;
            }
            else if (next > maxCode && codeSize < 12)
            {
                codeSize++;
                maxCode = codeSize == 12 ? MaxCodes : (1 << codeSize) - 1;
            }
        }

        Emit(Clear);
        int prefix = indices[0];
        for (int i = 1; i < indices.Length; i++)
        {
            int key = (prefix << 8) | indices[i];
            if (table.TryGetValue(key, out int code))
            {
                prefix = code;
                continue;
            }
            Emit(prefix);
            if (next < MaxCodes)
            {
                table[key] = next++;
            }
            else
            {
                table.Clear();
                next = End + 1;
                clearing = true;
                Emit(Clear);
            }
            prefix = indices[i];
        }
        Emit(prefix);
        Emit(End);
        if (bitCount > 0) block[blockLength++] = (byte)bitBuffer;
        FlushBlock();
        _out.WriteByte(0); // block terminator
    }

    void WriteUInt16(int value)
    {
        _out.WriteByte((byte)value);
        _out.WriteByte((byte)(value >> 8));
    }

    void WriteAscii(string text)
    {
        foreach (var ch in text) _out.WriteByte((byte)ch);
    }
}
