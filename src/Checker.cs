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
        using (Form owner = CreateOwner())
            return Run(args, owner);
    }

    // Invisible window that owns the file picker and the result box. Without it the process has no
    // window between closing the picker and showing the result, Windows hands the focus back to
    // Explorer/PowerShell, and the result box ends up behind it. It sits on the screen under the mouse,
    // so on multi-monitor setups both dialogs open on the screen the user is working on.
    static Form CreateOwner()
    {
        Form f = new Form();
        f.Text = Title;
        f.FormBorderStyle = FormBorderStyle.None;
        f.ShowInTaskbar = true;
        f.Opacity = 0;
        f.StartPosition = FormStartPosition.Manual;
        f.Size = new System.Drawing.Size(1, 1);
        System.Drawing.Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        f.Location = new System.Drawing.Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
        f.Show();
        f.Activate();
        return f;
    }

    static int Run(string[] args, Form owner)
    {
        FirmwareDb db;
        try
        {
            db = LoadEmbeddedDb();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "The firmware database inside this program is damaged:\n" + ex.Message,
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
                if (dlg.ShowDialog(owner) != DialogResult.OK) return 0;
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
        MessageBox.Show(owner, msg.ToString(), Title, MessageBoxButtons.OK, icon);
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
