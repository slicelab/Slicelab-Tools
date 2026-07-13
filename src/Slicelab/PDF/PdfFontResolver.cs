using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharpCore.Fonts;

namespace Slicelab.PDF
{
    public class PdfFontResolver : IFontResolver
    {
        private static readonly string[] FontDirs;
        private static bool _registered;

        static PdfFontResolver()
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix ||
                Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                FontDirs = new[]
                {
                    "/System/Library/Fonts/Supplemental",
                    "/System/Library/Fonts",
                    "/Library/Fonts",
                    Path.Combine(home, "Library/Fonts")
                };
            }
            else
            {
                FontDirs = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)),
                    @"C:\Windows\Fonts"
                };
            }
        }

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            GlobalFontSettings.FontResolver = new PdfFontResolver();
        }

        public string DefaultFontName => "Arial";

        public byte[] GetFont(string faceName)
        {
            string path = FindFontFile(faceName);
            if (path != null)
                return File.ReadAllBytes(path);

            // Fallback: try common .ttf-safe fonts
            foreach (var fallback in new[] { "Arial", "Verdana", "Courier New", "Geneva" })
            {
                path = FindFontFile(fallback);
                if (path != null)
                    return File.ReadAllBytes(path);
            }

            return null;
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            // Map Helvetica to Arial (Helvetica is .ttc on macOS)
            if (familyName.Equals("Helvetica", StringComparison.OrdinalIgnoreCase))
                familyName = "Arial";

            // Try styled variant file names (e.g. "Arial Bold Italic.ttf")
            string styledName = familyName;
            if (isBold && isItalic) styledName = familyName + " Bold Italic";
            else if (isBold) styledName = familyName + " Bold";
            else if (isItalic) styledName = familyName + " Italic";

            if (FindFontFile(styledName) != null)
                return new FontResolverInfo(styledName);

            // Fall back to base family with style simulation
            if (FindFontFile(familyName) != null)
                return new FontResolverInfo(familyName, isBold, isItalic);

            // Last resort: default font
            return new FontResolverInfo(DefaultFontName, isBold, isItalic);
        }

        /// <summary>
        /// Returns sorted list of font family names available as .ttf/.otf (no .ttc).
        /// Filters to base families only (excludes Bold/Italic/Light variants).
        /// </summary>
        public static List<string> GetAvailableFontNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var variantSuffixes = new[] { " Bold", " Italic", " Light", " Medium", " Thin",
                " Black", " Semibold", " ExtraBold", " ExtraLight", " Condensed",
                " BoldItalic", " Bold Italic", " Narrow" };

            foreach (var dir in FontDirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    var files = Directory.GetFiles(dir, "*.*")
                        .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

                    foreach (var file in files)
                    {
                        string baseName = Path.GetFileNameWithoutExtension(file);
                        // Skip variant files — keep base family only
                        bool isVariant = false;
                        foreach (var suffix in variantSuffixes)
                        {
                            if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                                baseName.EndsWith(suffix.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                            {
                                isVariant = true;
                                break;
                            }
                        }
                        if (!isVariant)
                            names.Add(baseName);
                    }
                }
                catch { }
            }

            var sorted = names.ToList();
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            return sorted;
        }

        private static string FindFontFile(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            string cleanName = name.Replace(" ", "").Replace("-", "");

            foreach (var dir in FontDirs)
            {
                if (!Directory.Exists(dir)) continue;

                // Try exact match (.ttf and .otf only — .ttc not supported by PdfSharpCore)
                foreach (var ext in new[] { ".ttf", ".otf" })
                {
                    string exact = Path.Combine(dir, name + ext);
                    if (File.Exists(exact)) return exact;

                    exact = Path.Combine(dir, cleanName + ext);
                    if (File.Exists(exact)) return exact;
                }

                // Scan directory for case-insensitive match
                try
                {
                    var files = Directory.GetFiles(dir, "*.*")
                        .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));

                    foreach (var file in files)
                    {
                        string fileBase = Path.GetFileNameWithoutExtension(file).Replace(" ", "").Replace("-", "");
                        if (string.Equals(fileBase, cleanName, StringComparison.OrdinalIgnoreCase))
                            return file;
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
