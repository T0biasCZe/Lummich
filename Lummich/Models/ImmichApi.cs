using System.Windows.Media.Imaging;
using WriteableBitmapEx = System.Windows.Media.Imaging.WriteableBitmap;
using System.Windows; // pro Rect
using System; // pro Math
using System.IO; // pro streamy
using System.IO.IsolatedStorage;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using Microsoft.Phone.Controls; // pokud používáte PhoneControls
using System.Diagnostics;
using System.Windows.Controls;
using Lummich.Models;
using System.Text;

public static class ImmichApi {
    private static string _key = "";

    public static string email = "";
    public static string password = "";

    public static List<string> LocalSSID = new List<string>();
    public static string LocalServerIp = "";
    public static string PublicServerUrl = "";

    public static string Username = "";
    public static string ProfileUrl = "";
    public static string ProfilePath = "";
    public static bool IsAdmin = false;

    public static string DeviceId {
        get {
            return Microsoft.Phone.Info.DeviceStatus.DeviceManufacturer + "_" + Microsoft.Phone.Info.DeviceStatus.DeviceName;
        }
    }

    public static async System.Threading.Tasks.Task NetworkTestAsync(System.Windows.Controls.TextBlock resultTextBlock) {
        if (resultTextBlock == null) return;
        resultTextBlock.Text = "";

        Debug.WriteLine("Starting Network test");

        // 1. Aktuální Wi-Fi
        string ssid = "";
        try {
            var icp = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (icp != null && icp.WlanConnectionProfileDetails != null) {
                ssid = icp.WlanConnectionProfileDetails.GetConnectedSsid();
            }
        } catch { }
        resultTextBlock.Text += LangHelper.GetString("currentWifi") + ": " + (string.IsNullOrEmpty(ssid) ? "-" : ssid) + "\n";
        Debug.WriteLine("SSID: " + ssid);

        // 2. Aktuální server
        string server = GetServerIP();
        resultTextBlock.Text += LangHelper.GetString("currentServer") + ": " + server + "\n";
        Debug.WriteLine("Server IP: " + server);


        // 3. Ping test
        resultTextBlock.Text += "Ping... ";
        bool pingResult = await TestConnectionAsync();
        if (pingResult) {
            resultTextBlock.Text += "Pong\n";
        } else {
            resultTextBlock.Text += LangHelper.GetString("pingFailed") + "\n";
        }

        // 4. Login test
        resultTextBlock.Text += LangHelper.GetString("loginTest") + "...\n";
        Debug.WriteLine("Trying to log in");
        string loginResult = await LoginAsync();
        if (loginResult.StartsWith("Success")) {
            resultTextBlock.Text += LangHelper.GetString("loginSuccess") + "\n";
            Debug.WriteLine("Login successful");
            resultTextBlock.Text += LangHelper.GetString("username") + ": " + Username + "\n";
            Debug.WriteLine("Username: " + Username);
            resultTextBlock.Text += LangHelper.GetString("isAdmin") + ": " + (IsAdmin ? LangHelper.GetString("yes") : LangHelper.GetString("no")) + "\n";
            Debug.WriteLine("IsAdmin: " + IsAdmin);
            resultTextBlock.Text += LangHelper.GetString("profileUrl") + ": " + ProfileUrl + "\n";
            Debug.WriteLine("ProfileUrl: " + ProfileUrl);
        } else {
            resultTextBlock.Text += loginResult + "\n";
        }
    }

    public static string GetServerIP() {
        try {
            var icp = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (icp != null && icp.WlanConnectionProfileDetails != null) {
                string ssid = icp.WlanConnectionProfileDetails.GetConnectedSsid();
                if (!string.IsNullOrEmpty(ssid) && LocalSSID != null && LocalSSID.Contains(ssid)) {
                    return LocalServerIp;
                }
            }
        } catch { /* ignore errors, fallback to public */ }
        return PublicServerUrl;
    }


    public static async System.Threading.Tasks.Task<bool> TestConnectionAsync() {
        string endpoint = GetServerIP() + "/api/server/ping";
        Debug.WriteLine("Testing connection to: " + endpoint);
        try {
            string reply = await HttpHelper.GetAsync(endpoint);
            if (!String.IsNullOrEmpty(reply)) {
                if (reply.Contains("pong")) return true;
            }
        } catch (Exception ex) {
            Debug.WriteLine("TestConnection error: " + ex.Message);
        }
        return false;
    }
    
    public static async System.Threading.Tasks.Task<string> LoginAsync() {
        string url = GetServerIP();
        string endpoint = url + "/api/auth/login";
        var json = $"{{\"email\":\"{email}\",\"password\":\"{password}\"}}";
        try {
            var result = await HttpHelper.PostJsonAsync(endpoint, json);
            if (!string.IsNullOrWhiteSpace(result)) {
                // Try parse as success
                try {
                    dynamic obj = Newtonsoft.Json.JsonConvert.DeserializeObject(result);
                    if (obj.accessToken != null) {
                        _key = (string)obj.accessToken;
                        Username = (string)obj.name;
                        ProfileUrl = (string)obj.profileImagePath;
                        IsAdmin = (bool)obj.isAdmin;
                        return "Success";
                    }
                    // else, try error
                    if (obj.error != null && obj.message != null) {
                        return $"Error: {(string)obj.error}\n{(string)obj.message}";
                    }
                } catch { }
                // fallback: return raw
                return "Unknown: " + result;
            }
        } catch (Exception ex) {
            Debug.WriteLine("Login error: " + ex.Message);
            return "Error: " + ex.Message;
        }
        return "Error: No response from server";
    }
    public static async Task<bool> UploadPhotoAsync(PhotoItem photo) {
        if (photo == null) return false;
        Debug.WriteLine("[UPLOAD] Starting upload for photo: " + photo.FullPath);

        // 1. Ping server
        bool pingOk = false;
        try {
            pingOk = await TestConnectionAsync();
        }
        catch { }
        if (!pingOk) {
            Debug.WriteLine("[UPLOAD] Server not reachable (ping failed)");
            return false;
        }
        Debug.WriteLine("[UPLOAD] Server reachable (ping successful)");

        // 2. Check API key
        if (string.IsNullOrEmpty(_key)) {
            Debug.WriteLine("[UPLOAD] API key missing, trying login...");
            string loginResult = await LoginAsync();
            if (!loginResult.StartsWith("Success")) {
                Debug.WriteLine("[UPLOAD] Login failed: " + loginResult);
                return false;
            }
        }
        Debug.WriteLine("[UPLOAD] API key present");

        // 3. Load file
        string filePath = !string.IsNullOrEmpty(photo.FullPathHR) ? photo.FullPathHR : photo.FullPath;
        if (string.IsNullOrEmpty(filePath)) {
            Debug.WriteLine("[UPLOAD] File path is empty");
            return false;
        }

        Windows.Storage.StorageFile file = null;
        try {
            file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
        }
        catch {
            Debug.WriteLine("[UPLOAD] File not found: " + filePath);
            return false;
        }

        string url = GetServerIP() + "/api/assets";
        string key = _key;
        string slug = photo.FileHash;

        // 4. Build multipart manually
        var boundary = "----ImmichBoundary" + DateTime.Now.Ticks;
        var newLine = "\r\n";
        var ms = new MemoryStream();
        var enc = Encoding.UTF8;

        Action<string> WriteString = (s) =>
        {
            var bytes = enc.GetBytes(s);
            ms.Write(bytes, 0, bytes.Length);
        };

        // deviceAssetId
        WriteString($"--{boundary}{newLine}");
        WriteString($"Content-Disposition: form-data; name=\"deviceAssetId\"{newLine}{newLine}");
        WriteString(photo.FileHash + newLine);

        // deviceId
        WriteString($"--{boundary}{newLine}");
        WriteString($"Content-Disposition: form-data; name=\"deviceId\"{newLine}{newLine}");
        WriteString(DeviceId + newLine);

        // duration (video only)
        if (photo.Type == MediaType.Video) {
            WriteString($"--{boundary}{newLine}");
            WriteString($"Content-Disposition: form-data; name=\"duration\"{newLine}{newLine}");
            WriteString(photo.LengthSeconds.ToString() + newLine);
        }

        // fileCreatedAt
        WriteString($"--{boundary}{newLine}");
        WriteString($"Content-Disposition: form-data; name=\"fileCreatedAt\"{newLine}{newLine}");
        WriteString(photo.DateTaken.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + newLine);

        // fileModifiedAt
        WriteString($"--{boundary}{newLine}");
        WriteString($"Content-Disposition: form-data; name=\"fileModifiedAt\"{newLine}{newLine}");
        WriteString(photo.DateTaken.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + newLine);

        // isFavorite
        WriteString($"--{boundary}{newLine}");
        WriteString($"Content-Disposition: form-data; name=\"isFavorite\"{newLine}{newLine}");
        WriteString((photo.IsUploaded ? "true" : "false") + newLine);

        // assetData (binary)
        byte[] fileBytes;
        using (var stream = await file.OpenStreamForReadAsync()) {
            fileBytes = new byte[stream.Length];
            await stream.ReadAsync(fileBytes, 0, fileBytes.Length);
        }

        WriteString($"--{boundary}{newLine}");
        WriteString($"Content-Disposition: form-data; name=\"assetData\"; filename=\"{Path.GetFileName(filePath)}\"{newLine}");
        WriteString($"Content-Type: application/octet-stream{newLine}{newLine}");
        ms.Write(fileBytes, 0, fileBytes.Length);
        WriteString(newLine);

        // End boundary
        WriteString($"--{boundary}--{newLine}");

        ms.Position = 0;

        Debug.WriteLine("[UPLOAD] Prepared multipart content, size: " + ms.Length + " bytes");

        // 5. SEND USING WINRT HTTPCLIENT
        var client = new Windows.Web.Http.HttpClient();

        // Authorization header
        client.DefaultRequestHeaders.Authorization =
            new Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", key);

        // Convert MemoryStream to IBuffer
        var buffer = ms.ToArray().AsBuffer();
        var content = new Windows.Web.Http.HttpBufferContent(buffer);

        // Set Content-Type with boundary
        content.Headers.ContentType =
            new Windows.Web.Http.Headers.HttpMediaTypeHeaderValue("multipart/form-data");
        content.Headers.ContentType.Parameters.Add(
            new Windows.Web.Http.Headers.HttpNameValueHeaderValue("boundary", boundary)
        );

        Debug.WriteLine("[UPLOAD] Sending upload request via WinRT HttpClient...");

        Windows.Web.Http.HttpResponseMessage response = null;

        try {
            //response = await client.PostAsync(new Uri(url + "?slug=" + Uri.EscapeDataString(slug)), content);
            response = await client.PostAsync(new Uri(url), content);
        }
        catch (Exception ex) {
            Debug.WriteLine("[UPLOAD ERROR] Exception during POST: " + ex);
            return false;
        }

        string respText = await response.Content.ReadAsStringAsync();
        Debug.WriteLine("[UPLOAD RESPONSE] " + respText);

        if (respText.Contains("\"status\":\"created\"") || respText.Contains("\"status\":\"duplicate\"") || respText.Contains("\"status\":\"replaced\"")) {
            photo.IsUploaded = true;
            return true;
        }


        Debug.WriteLine("[UPLOAD] Upload failed, status: " + response.StatusCode);
        return false;
    }

    public static void LoadAllSettings(System.Windows.Controls.TextBox emailBox, System.Windows.Controls.PasswordBox passwordBox, System.Windows.Controls.TextBox globalDomainBox, System.Windows.Controls.TextBox localIpBox, System.Windows.Controls.StackPanel ssidListPanel, List<System.Windows.Controls.TextBox> ssidTextBoxes, System.Action<string> addSsidTextBox) {
        // Email & Password
        if (IsolatedStorageSettings.ApplicationSettings.Contains("Email"))
            email = IsolatedStorageSettings.ApplicationSettings["Email"] as string;
        if (IsolatedStorageSettings.ApplicationSettings.Contains("Password"))
            password = IsolatedStorageSettings.ApplicationSettings["Password"] as string;

        if (emailBox != null && !String.IsNullOrEmpty(email)) emailBox.Text = email;
        if (passwordBox != null && !String.IsNullOrEmpty(password)) passwordBox.Password = password;

        // Server settings
        if (IsolatedStorageSettings.ApplicationSettings.Contains("GlobalDomain"))
            PublicServerUrl = IsolatedStorageSettings.ApplicationSettings["GlobalDomain"] as string;
        if (IsolatedStorageSettings.ApplicationSettings.Contains("LocalIp"))
            LocalServerIp = IsolatedStorageSettings.ApplicationSettings["LocalIp"] as string;
        if (IsolatedStorageSettings.ApplicationSettings.Contains("LocalSSID")) {
            var arr = IsolatedStorageSettings.ApplicationSettings["LocalSSID"] as string;
            if (!string.IsNullOrEmpty(arr))
                LocalSSID = new List<string>(arr.Split('|'));
            else
                LocalSSID = new List<string>();
        }
        if (globalDomainBox != null) globalDomainBox.Text = PublicServerUrl ?? "";
        if (localIpBox != null) localIpBox.Text = LocalServerIp ?? "";

        // SSL errors
        bool ignoreSsl = false;
        if (IsolatedStorageSettings.ApplicationSettings.Contains("IgnoreSslErrors"))
            bool.TryParse(IsolatedStorageSettings.ApplicationSettings["IgnoreSslErrors"].ToString(), out ignoreSsl);
        HttpHelper.IgnoreSSLErrors = ignoreSsl;

        // SSID list
        if (ssidListPanel != null) ssidListPanel.Children.Clear();
        if (ssidTextBoxes != null) ssidTextBoxes.Clear();
        if (LocalSSID != null && LocalSSID.Count > 0 && addSsidTextBox != null) {
            foreach (var ssid in LocalSSID) {
                addSsidTextBox(ssid);
            }
        }
    }

    public static void SaveAllSettings(System.Windows.Controls.TextBox emailBox, string password, System.Windows.Controls.TextBox globalDomainBox, System.Windows.Controls.TextBox localIpBox, List<System.Windows.Controls.TextBox> ssidTextBoxes) {
        // Email & Password
        if(emailBox != null & emailBox.Text.Length > 0) {
            email = emailBox.Text;
        }
        IsolatedStorageSettings.ApplicationSettings["Email"] = email;
        IsolatedStorageSettings.ApplicationSettings["Password"] = password;

        // Server settings
        PublicServerUrl = globalDomainBox != null ? globalDomainBox.Text ?? "" : "";
        LocalServerIp = localIpBox != null ? localIpBox.Text ?? "" : "";
        LocalSSID = new List<string>();
        if (ssidTextBoxes != null) {
            foreach (var tb in ssidTextBoxes) {
                if (tb != null && !string.IsNullOrWhiteSpace(tb.Text))
                    LocalSSID.Add(tb.Text.Trim());
            }
        }
        IsolatedStorageSettings.ApplicationSettings["GlobalDomain"] = PublicServerUrl;
        IsolatedStorageSettings.ApplicationSettings["LocalIp"] = LocalServerIp;
        IsolatedStorageSettings.ApplicationSettings["LocalSSID"] = string.Join("|", LocalSSID);
        // SSL errors
        IsolatedStorageSettings.ApplicationSettings["IgnoreSslErrors"] = HttpHelper.IgnoreSSLErrors;
        IsolatedStorageSettings.ApplicationSettings.Save();
    }
}
