// BuildDatabase.exe <folder> <database file>
// Fingerprints every .hex file in <folder> (recursively) and merges them into the database file:
//   - existing entries are always kept, even if their .hex file is no longer in <folder>
//   - new program code is appended, named after the file (file name without extension)
//   - program code that is already in the database (under any name) is not added again
// The database file is created if it does not exist yet.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

static class Builder
{
    static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: BuildDatabase.exe <folder with .hex files> <database file>");
            return 2;
        }
        string folder = args[0], dbPath = args[1];
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine("Folder not found: " + folder);
            return 2;
        }

        FirmwareDb db;
        if (File.Exists(dbPath))
        {
            db = FirmwareDb.Parse(File.ReadAllText(dbPath, Encoding.UTF8));
            if (db.Version != HexImage.FormatVersion)
            {
                Console.Error.WriteLine("ERROR: " + dbPath + " has format " + db.Version + ", expected " + HexImage.FormatVersion + ". Not touching it.");
                return 2;
            }
            if (db.BadLines.Count > 0)
            {
                Console.Error.WriteLine("ERROR: " + dbPath + " contains malformed lines. Fix them first, nothing was changed:");
                foreach (string l in db.BadLines) Console.Error.WriteLine("    " + l);
                return 2;
            }
            Console.WriteLine("Loaded " + db.Ordered.Count + " existing entries from " + dbPath);
        }
        else
        {
            db = new FirmwareDb();
            Console.WriteLine(dbPath + " does not exist yet, creating a new database.");
        }
        Console.WriteLine();

        string[] files = Directory.GetFiles(folder, "*.hex", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        int added = 0, known = 0, duplicates = 0, failed = 0;
        foreach (string f in files)
        {
            string name = Path.GetFileNameWithoutExtension(f);
            HexImage img;
            try
            {
                img = HexImage.Load(f);
            }
            catch (Exception ex)
            {
                Console.WriteLine("UNREADABLE  " + name + " - " + ex.Message);
                failed++;
                continue;
            }

            List<string> existing;
            if (db.Entries.TryGetValue(img.Fingerprint, out existing))
            {
                if (existing.Contains(name))
                {
                    Console.WriteLine("known       " + name);
                    known++;
                }
                else
                {
                    Console.WriteLine("DUPLICATE   " + name + " - identical program code is already in the database as \""
                                      + string.Join("\", \"", existing.ToArray()) + "\", not added");
                    duplicates++;
                }
                continue;
            }

            if (db.HasName(name))
            {
                Console.WriteLine("NAME TAKEN  " + name + " - this name is already used for different program code, not added."
                                  + " Rename the file and run again.");
                failed++;
                continue;
            }
            db.Add(img.Fingerprint, name);
            added++;
            Console.WriteLine("ADDED       " + name);
            Console.WriteLine("    " + img.Describe().Replace("\n", "\n    "));
            Console.WriteLine("    Fingerprint: " + img.Fingerprint);
            if (img.ConfigStart < 0)
                Console.WriteLine("    WARNING: no config area detected - check this dump!");
        }

        if (added > 0)
            File.WriteAllText(dbPath, db.Serialize(), new UTF8Encoding(false));

        Console.WriteLine();
        Console.WriteLine(added + " added, " + known + " already known, " + duplicates + " duplicate(s) not added, "
                          + failed + " not added due to errors. Database now has " + db.Ordered.Count + " entries"
                          + (added > 0 ? " -> " + dbPath : " (unchanged)") + ".");
        return failed > 0 ? 1 : 0;
    }
}
