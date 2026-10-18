// Shared Intel HEX parsing and fingerprinting logic.
// Used by both the checker (AcebeamHexChecker.exe) and the database builder (BuildDatabase.exe).
//
// Fingerprint algorithm (format version 1):
//   1. Parse every data record into a byte-addressed memory map (record types 00/02/04 honoured,
//      checksums verified). Text encoding, line endings, record length and record order do not matter.
//   2. Split off the config/calibration area: walking the contiguous data segments in address order,
//      the first segment that (a) is not the first segment, (b) starts after a gap and (c) starts at a
//      power-of-two byte address >= 0x4000 marks the start of the config area. Everything from there
//      upwards (config words, calibration values, user IDs, EEPROM, ...) is ignored.
//      Known layouts: 2K-word chips -> config at byte 0x4000, 4K/8K-word chips -> config at byte 0x8000.
//   3. The remaining program area is read as little-endian 16-bit words, masked to 14 bits
//      (the MCU is a 14-bit core), missing bytes count as erased (0xFF). Trailing erased words
//      (0x3FFF) are dropped, so dumps with or without blank padding give the same result.
//   4. SHA-256 over the resulting word image = fingerprint.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public class HexFormatException : Exception
{
    public HexFormatException(string message) : base(message) { }
}

public class HexImage
{
    public const int FormatVersion = 1;
    const int MinConfigBase = 0x4000;

    public SortedDictionary<long, byte> Memory = new SortedDictionary<long, byte>();
    public List<long[]> Segments = new List<long[]>();   // [start, end] inclusive, byte addresses
    public long ProgramEnd;      // exclusive byte address where the program area ends (= config base, or end of data)
    public long ProgramLast;     // highest byte address with data in the program area
    public long ConfigStart = -1; // -1 if no config area found
    public long ConfigEnd = -1;   // inclusive
    public int ConfigBytes;
    public int ProgramWords;     // words hashed after trimming blank tail
    public string Fingerprint;

    public static HexImage Load(string path)
    {
        return Parse(File.ReadAllBytes(path));
    }

    public static HexImage Parse(byte[] raw)
    {
        HexImage img = new HexImage();
        img.ReadRecords(DecodeText(raw));
        if (img.Memory.Count == 0)
            throw new HexFormatException("The file does not contain any firmware data.");
        img.BuildSegments();
        img.SplitConfig();
        img.ComputeFingerprint();
        return img;
    }

    // Accepts UTF-8 / UTF-16 LE/BE / UTF-32 (with or without BOM) and plain ASCII/ANSI.
    static string DecodeText(byte[] b)
    {
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0)
            return new UTF32Encoding(false, false).GetString(b, 4, b.Length - 4);
        if (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF)
            return new UTF32Encoding(true, false).GetString(b, 4, b.Length - 4);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);

        // No BOM: guess UTF-16 from the position of zero bytes.
        int zeroEven = 0, zeroOdd = 0;
        int n = Math.Min(b.Length, 4096);
        for (int i = 0; i < n; i++)
            if (b[i] == 0) { if ((i & 1) == 0) zeroEven++; else zeroOdd++; }
        if (zeroOdd > n / 4 && zeroEven == 0) return Encoding.Unicode.GetString(b);
        if (zeroEven > n / 4 && zeroOdd == 0) return Encoding.BigEndianUnicode.GetString(b);

        // Hex records are pure ASCII, so Latin-1 never fails to decode.
        return Encoding.GetEncoding(28591).GetString(b);
    }

    static bool IsHex(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    void ReadRecords(string text)
    {
        // Split on ':' instead of line breaks: robust against any line ending style,
        // stray whitespace and even files that lost all their line breaks.
        string[] chunks = text.Split(':');
        if (chunks.Length < 2)
            throw new HexFormatException("This is not an Intel HEX file (no ':' records found).");

        long upper = 0;   // from type 02 / 04 records
        int recordNo = 0;
        bool sawEof = false;
        for (int c = 1; c < chunks.Length && !sawEof; c++)
        {
            recordNo++;
            StringBuilder sb = new StringBuilder();
            foreach (char ch in chunks[c])
            {
                if (IsHex(ch)) sb.Append(ch);
                else if (char.IsWhiteSpace(ch) || ch == '\0') continue;
                else throw new HexFormatException("Invalid character '" + ch + "' in record " + recordNo + ".");
            }
            string hex = sb.ToString();
            if (hex.Length < 10 || (hex.Length & 1) != 0)
                throw new HexFormatException("Record " + recordNo + " is truncated or malformed.");

            byte[] rec = new byte[hex.Length / 2];
            for (int i = 0; i < rec.Length; i++)
                rec[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            int len = rec[0];
            if (rec.Length != len + 5)
                throw new HexFormatException("Record " + recordNo + " has a wrong length.");
            int sum = 0;
            foreach (byte x in rec) sum += x;
            if ((sum & 0xFF) != 0)
                throw new HexFormatException("Checksum error in record " + recordNo + " - the file is corrupted.");

            int addr = (rec[1] << 8) | rec[2];
            int type = rec[3];
            switch (type)
            {
                case 0x00:
                    for (int i = 0; i < len; i++)
                        Memory[upper + addr + i] = rec[4 + i];
                    break;
                case 0x01:
                    sawEof = true;
                    break;
                case 0x02:
                    upper = (long)((rec[4] << 8) | rec[5]) << 4;
                    break;
                case 0x04:
                    upper = (long)((rec[4] << 8) | rec[5]) << 16;
                    break;
                case 0x03:
                case 0x05:
                    break; // start address records - irrelevant for the image
                default:
                    throw new HexFormatException("Unknown record type " + type + " in record " + recordNo + ".");
            }
        }
    }

    void BuildSegments()
    {
        long start = -1, prev = -1;
        foreach (long a in Memory.Keys)
        {
            if (start < 0) { start = a; }
            else if (a != prev + 1) { Segments.Add(new long[] { start, prev }); start = a; }
            prev = a;
        }
        Segments.Add(new long[] { start, prev });
    }

    static bool IsPowerOfTwo(long v) { return v > 0 && (v & (v - 1)) == 0; }

    void SplitConfig()
    {
        for (int i = 1; i < Segments.Count; i++)
        {
            long s = Segments[i][0];
            if (s >= MinConfigBase && IsPowerOfTwo(s))
            {
                ConfigStart = s;
                break;
            }
        }
        foreach (long[] seg in Segments)
            if (ConfigStart < 0 || seg[0] < ConfigStart) ProgramLast = seg[1];
        if (ConfigStart >= 0)
        {
            ProgramEnd = ConfigStart;
            foreach (KeyValuePair<long, byte> kv in Memory)
                if (kv.Key >= ConfigStart) { ConfigBytes++; ConfigEnd = kv.Key; }
        }
        else
        {
            ProgramEnd = Segments[Segments.Count - 1][1] + 1;
        }
    }

    void ComputeFingerprint()
    {
        int totalWords = (int)((ProgramEnd + 1) / 2);
        int[] words = new int[totalWords];
        for (int w = 0; w < totalWords; w++)
        {
            byte lo, hi;
            if (!Memory.TryGetValue(2L * w, out lo)) lo = 0xFF;
            if (!Memory.TryGetValue(2L * w + 1, out hi)) hi = 0xFF;
            words[w] = (lo | (hi << 8)) & 0x3FFF;
        }
        int used = totalWords;
        while (used > 0 && words[used - 1] == 0x3FFF) used--;
        ProgramWords = used;

        byte[] image = new byte[used * 2];
        for (int w = 0; w < used; w++)
        {
            image[2 * w] = (byte)(words[w] & 0xFF);
            image[2 * w + 1] = (byte)(words[w] >> 8);
        }
        using (SHA256 sha = SHA256.Create())
        {
            byte[] h = sha.ComputeHash(image);
            StringBuilder sb = new StringBuilder();
            foreach (byte x in h) sb.Append(x.ToString("x2"));
            Fingerprint = sb.ToString();
        }
    }

    public string Describe()
    {
        string s = "Program area: 0x0000-0x" + ProgramLast.ToString("X4") + " (" + ProgramWords + " words compared)";
        if (ConfigStart >= 0)
            s += "\nIgnored config/calibration area: 0x" + ConfigStart.ToString("X4") + "-0x" + ConfigEnd.ToString("X4")
                 + " (" + ConfigBytes + " bytes)";
        else
            s += "\nNo config/calibration area found (nothing ignored)";
        return s;
    }
}

// The fingerprint database: one "fingerprint  name" pair per line, '#' starts a comment.
public class FirmwareDb
{
    public Dictionary<string, List<string>> Entries = new Dictionary<string, List<string>>();
    public List<string[]> Ordered = new List<string[]>();   // [fingerprint, name] in file order
    public List<string> BadLines = new List<string>();
    public int Version = -1;

    public static FirmwareDb Parse(string text)
    {
        FirmwareDb db = new FirmwareDb();
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("# format"))
            {
                int v;
                if (int.TryParse(line.Substring(8).Trim(), out v)) db.Version = v;
                continue;
            }
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int sp = line.IndexOfAny(new char[] { ' ', '\t' });
            string name = sp == 64 ? line.Substring(sp).Trim() : "";
            if (sp != 64 || name.Length == 0) { db.BadLines.Add(line); continue; }
            db.Add(line.Substring(0, 64).ToLowerInvariant(), name);
        }
        return db;
    }

    public void Add(string fingerprint, string name)
    {
        List<string> names;
        if (!Entries.TryGetValue(fingerprint, out names)) { names = new List<string>(); Entries[fingerprint] = names; }
        names.Add(name);
        Ordered.Add(new string[] { fingerprint, name });
    }

    public bool HasName(string name)
    {
        foreach (string[] e in Ordered)
            if (string.Equals(e[1], name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public string Serialize()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("# Acebeam HEX checker - reference firmware fingerprints\n");
        sb.Append("# format ").Append(HexImage.FormatVersion).Append("\n");
        sb.Append("# <sha256 of program area>  <reference name>\n");
        sb.Append("# Entries are only ever appended by update_database.bat; edit names by hand if needed.\n");
        foreach (string[] e in Ordered)
            sb.Append(e[0]).Append("  ").Append(e[1]).Append("\n");
        return sb.ToString();
    }
}
