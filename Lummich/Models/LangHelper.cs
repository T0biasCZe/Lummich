using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Resources;
using System.Globalization;

public static class LangHelper
{
    private static Dictionary<string, Dictionary<string, string>> _langCache = new Dictionary<string, Dictionary<string, string>>();
    private static string[] fallbackOrder = null;


    static LangHelper()
    {
        // Determine fallback order based on system language and language mappings
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
        string mappedLang = null;
        try
        {
            string mapPath = "Assets/Language/maps.txt";
            StreamResourceInfo sri = Application.GetResourceStream(new Uri(mapPath, UriKind.Relative));
            if (sri != null)
            {
                using (var reader = new StreamReader(sri.Stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        var parts = line.Split(';');
                        if (parts.Length == 2 && parts[0].Trim().ToLower() == lang)
                        {
                            mappedLang = parts[1].Trim().ToLower();
                            break;
                        }
                    }
                }
            }
        }
        catch { }

        if (!string.IsNullOrEmpty(mappedLang))
        {
            fallbackOrder = new[] { lang, mappedLang, "en", "cs" };
        }
        else if (lang == "cs")
            fallbackOrder = new[] { "cs", "en" };
        else if (lang == "en")
            fallbackOrder = new[] { "en", "cs" };
        else
            fallbackOrder = new[] { lang, "en", "cs" };
    }

    public static string GetString(string key)
    {
        foreach (var lang in fallbackOrder)
        {
            var dict = GetLangDict(lang);
            if (dict != null && dict.ContainsKey(key))
                return dict[key];
        }
        // Not found in any language
        return key;
    }

    private static Dictionary<string, string> GetLangDict(string lang)
    {
        if (_langCache.ContainsKey(lang))
            return _langCache[lang];

        try
        {
            var dict = new Dictionary<string, string>();
            string path = $"Assets/Language/{lang}.txt";
            StreamResourceInfo sri = Application.GetResourceStream(new Uri(path, UriKind.Relative));
            if (sri == null) return null;
            using (var reader = new StreamReader(sri.Stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int idx = line.IndexOf('=');
                    if (idx > 0)
                    {
                        var k = line.Substring(0, idx).Trim();
                        var v = line.Substring(idx + 1).Trim();
                        dict[k] = v;
                    }
                }
            }
            _langCache[lang] = dict;
            return dict;
        }
        catch
        {
            _langCache[lang] = null;
            return null;
        }
    }
}
