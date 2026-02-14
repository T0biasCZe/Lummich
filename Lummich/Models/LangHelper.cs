using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Resources;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;
using System.Diagnostics;
using Microsoft.Phone.Controls;

public static class LangHelper {
    private static Dictionary<string, Dictionary<string, string>> _langCache = new Dictionary<string, Dictionary<string, string>>();
    private static string[] fallbackOrder = null;

    private static string overwriteLang = null;


    static LangHelper() {
        // Determine fallback order based on system language and language mappings
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();
        
        if(!string.IsNullOrEmpty(overwriteLang)) lang = overwriteLang;

        string mappedLang = null;
        try {
            string mapPath = "Assets/Language/maps.txt";
            StreamResourceInfo sri = Application.GetResourceStream(new Uri(mapPath, UriKind.Relative));
            if (sri != null) {
                using (var reader = new StreamReader(sri.Stream)) {
                    string line;
                    while ((line = reader.ReadLine()) != null) {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        var parts = line.Split(';');
                        if (parts.Length == 2 && parts[0].Trim().ToLower() == lang) {
                            mappedLang = parts[1].Trim().ToLower();
                            break;
                        }
                    }
                }
            }
        }
        catch { }

        if (!string.IsNullOrEmpty(mappedLang)) {
            fallbackOrder = new[] { lang, mappedLang, "en", "cs" };
        }
        else if (lang == "cs")
            fallbackOrder = new[] { "cs", "en" };
        else if (lang == "en")
            fallbackOrder = new[] { "en", "cs" };
        else
            fallbackOrder = new[] { lang, "en", "cs" };
    }

    public static string GetString(string key) {
        foreach (var lang in fallbackOrder) {
            var dict = GetLangDict(lang);
            if (dict != null && dict.ContainsKey(key))
                return dict[key];
        }
        return key;
    }

    private static Dictionary<string, string> GetLangDict(string lang) {
        if (_langCache.ContainsKey(lang))
            return _langCache[lang];

        try {
            var dict = new Dictionary<string, string>();
            string path = $"Assets/Language/{lang}.txt";
            StreamResourceInfo sri = Application.GetResourceStream(new Uri(path, UriKind.Relative));
            if (sri == null) return null;
            using (var reader = new StreamReader(sri.Stream)) {
                string line;
                while ((line = reader.ReadLine()) != null) {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int idx = line.IndexOf('=');
                    if (idx > 0) {
                        var k = line.Substring(0, idx).Trim();
                        var v = line.Substring(idx + 1).Trim();
                        dict[k] = v;
                    }
                }
            }
            _langCache[lang] = dict;
            return dict;
        }
        catch {
            _langCache[lang] = null;
            return null;
        }
    }

    public static void TranslatePage(PhoneApplicationPage page, string pageName) {
        if (page == null || string.IsNullOrEmpty(pageName)) return;

        // čeština je hardcoded v XAML
        if (fallbackOrder.Length > 0 && fallbackOrder[0] == "cs") {
            Debug.WriteLine($"[LANG] Skipping translation for {pageName} - Czech is the default language in XAML");
            return;
        }

        Debug.WriteLine($"[LANG] Translating page: {pageName}");

        // Načíst překlady jednou pro celou stránku
        var translations = LoadPageTranslations(pageName);
        if (translations == null || translations.Count == 0) {
            Debug.WriteLine($"[LANG] No translations found for page: {pageName}");
            return;
        }

        Debug.WriteLine($"[LANG] Loaded {translations.Count} translations for page: {pageName}");

        // Projít vizuální strom a přeložit všechny elementy
        TranslateVisualTree(page, translations);
    }

    private static Dictionary<string, string> LoadPageTranslations(string pageName) {
        // Zkusit načíst pro každý jazyk v fallback pořadí
        foreach (var lang in fallbackOrder) {
            string cacheKey = $"{pageName}.{lang}";
            
            //Debug.WriteLine($"[LANG] Trying language: {lang}");
            
            // Zkontrolovat cache
            if (_langCache.ContainsKey(cacheKey)) {
                if (_langCache[cacheKey] != null && _langCache[cacheKey].Count > 0) {
                    //Debug.WriteLine($"[LANG] Found in cache: Assets/Language/{pageName}.{lang}.txt");
                    return _langCache[cacheKey];
                }
                continue;
            }

            // Načíst ze souboru
            try {
                var dict = new Dictionary<string, string>();
                string path = $"Assets/Language/{pageName}.{lang}.txt";
                StreamResourceInfo sri = Application.GetResourceStream(new Uri(path, UriKind.Relative));
                
                if (sri != null) {
                    //Debug.WriteLine($"[LANG] Successfully loaded: {path}");
                    
                    using (var reader = new StreamReader(sri.Stream)) {
                        string line;
                        while ((line = reader.ReadLine()) != null) {
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) 
                                continue;
                            
                            int idx = line.IndexOf('=');
                            if (idx > 0) {
                                var key = line.Substring(0, idx).Trim();
                                var value = line.Substring(idx + 1).Trim();
                                dict[key] = value;
                            }
                        }
                    }
                    
                    _langCache[cacheKey] = dict;
                    if (dict.Count > 0) {
                        //Debug.WriteLine($"[LANG] Selected language: {lang} with {dict.Count} entries");
                        return dict;
                    }
                }
                else {
                    //Debug.WriteLine($"[LANG] File not found: {path}");
                    _langCache[cacheKey] = null;
                }
            }
            catch (Exception ex) {
                Debug.WriteLine($"[LANG] Error loading {pageName}.{lang}.txt: {ex.Message}");
                _langCache[cacheKey] = null;
            }
        }
        
        Debug.WriteLine($"[LANG] No translation files found for page: {pageName}");
        return new Dictionary<string, string>();
    }

    private static void TranslateElement(FrameworkElement element, Dictionary<string, string> translations) {
        if (element == null || string.IsNullOrEmpty(element.Name))
            return;

        if (!translations.ContainsKey(element.Name))
            return;

        string translatedText = translations[element.Name];

        // Nastavit správnou vlastnost podle typu elementu
        switch (element.GetType().Name)
        {
            case "TextBlock":
                ((TextBlock)element).Text = translatedText;
                break;
            case "Button":
                ((Button)element).Content = translatedText;
                break;
            case "PanoramaItem":
                ((PanoramaItem)element).Header = translatedText;
                break;
            case "PivotItem":
                ((PivotItem)element).Header = translatedText;
                break;
            case "CheckBox":
                ((CheckBox)element).Content = translatedText;
                break;
        }
    }

    private static void TranslateVisualTree(DependencyObject parent, Dictionary<string, string> translations) {
        if (parent == null) return;

        // Přeložit aktuální element
        if (parent is FrameworkElement) {
            var element = (FrameworkElement)parent;
            //Debug.WriteLine($"[LANG TREE] Checking: {element.GetType().Name} Name={element.Name}");
            
            if (!string.IsNullOrEmpty(element.Name) && translations.ContainsKey(element.Name)) {
                TranslateElement(element, translations);
                //Debug.WriteLine($"[LANG] Translated: {element.Name} ({element.GetType().Name})");
            }
        }

        // Projít všechny potomky
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        //Debug.WriteLine($"[LANG TREE] {parent.GetType().Name} has {childCount} children");
        
        for (int i = 0; i < childCount; i++) {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            TranslateVisualTree(child, translations);
        }
    }
}
