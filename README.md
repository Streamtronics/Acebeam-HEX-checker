# Acebeam HEX Checker

Checks whether a firmware dump (Intel HEX) from an Acebeam light matches one of the reference dumps,
without shipping the reference firmware itself. Only SHA-256 fingerprints are distributed.

## For users

Drag the dumped `.hex` file onto `AcebeamHexChecker.exe`, or double-click the exe and pick the file.
A message box says **MATCH FOUND** (with the reference name) or **NO MATCH FOUND**.
Several files can be dropped at once.

## Contributing dumps

`firmware_db.txt` is the shared database: one `<fingerprint>  <name>` line per known firmware.
The dumps themselves are never committed (`References/` is in `.gitignore`).

1. Put your `.hex` dumps into `References\` (subfolders are fine). The file name without the
   extension becomes the entry's name, e.g. `X35_v2.hex` becomes `X35_v2`. Use a descriptive name
   (model, version or hardware revision).
2. Double-click `update_database.bat`. It merges your dumps into `firmware_db.txt`:
   - Existing entries are always kept, even if you don't have their `.hex` files.
   - Dumps with new program code are appended (`ADDED`).
   - Dumps whose program code is already in the database are not added, even under a different
     name. The script prints `DUPLICATE ... already in the database as "<name>"`.
   - Dumps with new program code but an already-used name are not added (`NAME TAKEN`). Rename the
     file and run again.
3. Commit the changed `firmware_db.txt` and open a pull request.

The script also compiles `dist\AcebeamHexChecker.exe` with the database embedded. Building only needs
the C# compiler that ships with Windows (.NET Framework 4). Nothing has to be installed.

To rename or remove an entry, edit `firmware_db.txt` by hand.

## How the comparison works

The comparison is based on memory addresses, not on line numbers:

* The file is parsed into a memory map. Encoding (ANSI/UTF-8/UTF-16/UTF-32, with or without BOM),
  line endings, upper/lower case, record length and record order make no difference. Checksums are verified.
* The config/calibration data sits in its own block at a power-of-two address, separated from the
  program by a gap: byte address `0x4000` on the 2K-word chips (M1, PT20) and `0x8000` on the
  larger chips (K1, M2-X, UC25, UC3A, X35). The first such block, and everything above it, is ignored.
* The program area is read as 14-bit words. Blank flash (`FFFF`/`3FFF`) at the end is trimmed,
  so dumps with or without blank padding give the same result.
* The SHA-256 of that program image is the fingerprint.

The algorithm lives in `src/HexImage.cs`. If you change it in a way that changes the fingerprints,
bump `FormatVersion` there and regenerate the database from the original dumps.
