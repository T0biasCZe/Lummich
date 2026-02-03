using System.Collections.Generic;
using System.IO.IsolatedStorage;

public static class FolderManager {

    private const string Key = "SelectedFolders";

    public static List<string> LoadFolders() {
        var settings = IsolatedStorageSettings.ApplicationSettings;

        if (settings.Contains(Key)) {
            return (List<string>)settings[Key];
        }

        return new List<string>();
    }

    public static void SaveFolders(List<string> folders) {
        var settings = IsolatedStorageSettings.ApplicationSettings;
        settings[Key] = folders;
        settings.Save();
    }
}
