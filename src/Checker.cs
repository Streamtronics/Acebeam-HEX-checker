// AcebeamHexChecker.exe - drag a dumped .hex file onto the exe (or double-click it and pick a file).
// The fingerprint database (firmware_db.txt) is embedded into the exe at build time.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

static class Checker
{
    const string Title = "Acebeam HEX Checker";

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();

        FirmwareDb db;
        try
        {
            db = LoadEmbeddedDb();
        }
        catch (Exception ex)
        {
            MessageBox.Show("The firmware database inside this program is damaged:\n" + ex.Message,
                Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 3;
        }

        List<string> files = new List<string>(args);
        if (files.Count == 0)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "Select a dumped firmware .hex file";
                dlg.Filter = "Intel HEX files (*.hex)|*.hex|All files (*.*)|*.*";
                dlg.Multiselect = true;
                if (dlg.ShowDialog() != DialogResult.OK) return 0;
                files.AddRange(dlg.FileNames);
            }
        }

        StringBuilder msg = new StringBuilder();
        bool anyMatch = false, anyError = false;
        foreach (string f in files)
        {
            if (msg.Length > 0) msg.Append("\n\n----------------------------------------\n\n");
            msg.Append("File: ").Append(Path.GetFileName(f)).Append("\n\n");
            try
            {
                HexImage img = HexImage.Load(f);
                List<string> names;
                if (db.Entries.TryGetValue(img.Fingerprint, out names))
                {
                    anyMatch = true;
                    msg.Append("MATCH FOUND\n\nMatches reference firmware:\n");
                    foreach (string n in names) msg.Append("    ").Append(n).Append("\n");
                }
                else
                {
                    msg.Append("NO MATCH FOUND\n\nThis firmware does not match any of the ")
                       .Append(db.Entries.Count).Append(" known reference firmwares.\n");
                }
                msg.Append("\n").Append(img.Describe());
            }
            catch (HexFormatException ex)
            {
                anyError = true;
                msg.Append("ERROR: Could not read this file.\n").Append(ex.Message);
            }
            catch (Exception ex)
            {
                anyError = true;
                msg.Append("ERROR: ").Append(ex.Message);
            }
        }

        MessageBoxIcon icon = anyError ? MessageBoxIcon.Error
                            : anyMatch ? MessageBoxIcon.Information
                            : MessageBoxIcon.Warning;
        MessageBox.Show(msg.ToString(), Title, MessageBoxButtons.OK, icon);
        return anyError ? 2 : anyMatch ? 0 : 1;
    }

    static FirmwareDb LoadEmbeddedDb()
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        using (Stream s = asm.GetManifestResourceStream("firmware_db.txt"))
        {
            if (s == null) throw new Exception("firmware_db.txt resource missing.");
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
            {
                FirmwareDb db = FirmwareDb.Parse(r.ReadToEnd());
                if (db.Version != HexImage.FormatVersion)
                    throw new Exception("Database format " + db.Version + " does not match program format " + HexImage.FormatVersion + ".");
                return db;
            }
        }
    }
}
