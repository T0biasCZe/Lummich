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
using System.Linq;
using Windows.Web.Http;
using Lummich;

public static class ImmichServerStats {
    // /api/server/storage
    public static ulong DiskUsage;  //diskUseRaw 
    public static ulong DiskCapacity; //diskSizeRaw 

    public static int MediaSentToServerCount;


    public static async Task RefreshStats() {
        bool pingOk = false;
        try { pingOk = await ImmichApi.TestConnectionAsync(); } catch { }
        if (!pingOk) {
            Debug.WriteLine("[STATS] Server not reachable (ping failed)");
        }
        Debug.WriteLine("[STATS] Server reachable (ping successful)");

        // 2. Check API key
        if (string.IsNullOrEmpty(ImmichApi._key)) {
            Debug.WriteLine("[STATS] API key missing, trying login...");
            string loginResult = await ImmichApi.LoginAsync();
            if (!loginResult.StartsWith("Success")) {
                Debug.WriteLine("[STATS] Login failed: " + loginResult);
            }
        }
        Debug.WriteLine("[STATS] API key present");


        //obtain number of media already uploaded to server, on endpoint /api/assets/device/{ImmichApi.DeviceId}
        string url = ImmichApi.GetServerIP() + "/api/assets/device/" + ImmichApi.DeviceId;
        try {
            string reply = await HttpHelper.GetAsync(url, ImmichApi._key);
            if (!String.IsNullOrEmpty(reply)) {
                //Debug.WriteLine("[STATS] Received stats response:\n" + reply);
                int quoteCount = 0;
                foreach (char c in reply)
                {
                    if (c == '"') quoteCount++;
                }
                MediaSentToServerCount = quoteCount / 2;
            }
        } catch (Exception ex) {
            Debug.WriteLine("Stats error: " + ex.Message);
        }

        //get storage stats on endpoint /api/server/storage
        url = ImmichApi.GetServerIP() + "/api/server/storage";
        try {
            string reply = await HttpHelper.GetAsync(url, ImmichApi._key);
            if (!String.IsNullOrEmpty(reply)) {
                Debug.WriteLine("[STATS] Received storage stats response:\n" + reply);
                try {
                    dynamic obj = Newtonsoft.Json.JsonConvert.DeserializeObject(reply);
                    DiskUsage = (ulong)obj.diskUseRaw;
                    DiskCapacity = (ulong)obj.diskSizeRaw;
                    Debug.WriteLine($"[STATS] Disk usage: {DiskUsage} bytes, capacity: {DiskCapacity} bytes");
                } catch (Exception ex) {
                    Debug.WriteLine("Stats parsing error: " + ex.Message);
                }
            }
        } catch (Exception ex) {
            Debug.WriteLine("Stats error: " + ex.Message);
        }
    }
}
public static class ImmichApi {
    public static string _key = "";

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
        Debug.WriteLine("[LOGIN] Trying to login with email " + email);
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

    public static async Task<bool> BulkUploadAsync(List<PhotoItem> photos, Action<int, int, string, ulong, ulong, ulong, ulong> photoProgressCallback = null) {
        bool pingOk = false;
        try { pingOk = await TestConnectionAsync(); } catch { }
        if (!pingOk) {
            Debug.WriteLine("[BULK UPLOAD] Server not reachable (ping failed)");
            return false;
        }
        Debug.WriteLine("[BULK UPLOAD] Server reachable (ping successful)");

        // 2. Check API key
        if (string.IsNullOrEmpty(_key)) {
            Debug.WriteLine("[BULK UPLOAD] API key missing, trying login...");
            string loginResult = await LoginAsync();
            if (!loginResult.StartsWith("Success")) {
                Debug.WriteLine("[BULK UPLOAD] Login failed: " + loginResult);
                return false;
            }
        }

        Debug.WriteLine("[BULK UPLOAD] Updating upload status for " + photos.Count + " photos...");
        //update isUploaded flag for all photos. Go through all photos, and if FileHash of the photo is in the list from server, set isUploaded to true, otherwise false.
        string url = ImmichApi.GetServerIP() + "/api/assets/device/" + ImmichApi.DeviceId; //returns list of hashes, if format ["hash1","hash2",...]
        var jsonList = new List<string>();
        try {
            string reply = await HttpHelper.GetAsync(url, _key);
            if (!String.IsNullOrEmpty(reply)) {
                jsonList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(reply);
            }
        } catch (Exception ex) {
            Debug.WriteLine("[BULK UPLOAD] Bulk upload error: " + ex.Message);
        }
        Debug.WriteLine($"[BULK UPLOAD] Received json with {jsonList.Count()} elements");
        foreach (var photo in photos) {
            if (jsonList.Contains(photo.FileHash)) {
                photo.IsUploaded = true;
            } else {
                photo.IsUploaded = false;
            }
        }
        Debug.WriteLine("[BULK UPLOAD] Upload status updated for " + photos.Count + " photos. Starting upload...");
        Debug.WriteLine("[BULK UPLOAD] Starting bulk upload of " + photos.Count + " photos...");

        // Prepare session stats
        var toUpload = photos.Where(p => !p.IsUploaded).ToList();
        int sessionUploadCount = toUpload.Count;
        int alreadyUploadedCount = 0;
        ulong sessionTotalBytes = 0;
        foreach (var p in toUpload) {
            sessionTotalBytes += p.SizeBytesHR > 0 ? p.SizeBytesHR : p.SizeBytes;
        }

        ulong sessionUploadedBytes = 0;

        for (int i = 0; i < photos.Count; i++) {
            try {
                var photo = photos[i];
                ulong fileSize = photo.SizeBytesHR > 0 ? photo.SizeBytesHR : photo.SizeBytes;
                string currentFile = photo.FullPathHR ?? photo.FullPath;
                currentFile = currentFile.Replace("C:\\Users\\Public\\Pictures", "").Replace("C:\\Data\\Users\\Public\\Pictures", "");
                ulong currentFileUploaded = 0;

                if (photo.IsUploaded) {
                    Debug.WriteLine($"[BULK UPLOAD] Photo {i + 1}/{photos.Count} already uploaded, skipping: {photo.FullPath}");
                    // Report progress for skipped file
                    photoProgressCallback?.Invoke(
                        sessionUploadCount,
                        alreadyUploadedCount,
                        currentFile,
                        fileSize,
                        fileSize,
                        sessionUploadedBytes,
                        sessionTotalBytes
                    );
                    continue;
                }

                // Progress callback for current file
                Debug.WriteLine("[BULK UPLOAD] starting uploadphotoasync");
                bool result = await UploadPhotoAsync(photo, (sent, total) => {
                    // sent = bytes uploaded for current file
                    photoProgressCallback?.Invoke(
                        sessionUploadCount,
                        alreadyUploadedCount,
                        currentFile,
                        (ulong)sent,
                        fileSize,
                        sessionUploadedBytes + (ulong)sent,
                        sessionTotalBytes
                    );
                }, true);
                Debug.WriteLine("[BULK UPLOAD] uploadphotoasync end");


                if (result) {
                    sessionUploadedBytes += fileSize;
                    Debug.WriteLine($"[BULK UPLOAD] Photo {i + 1}/{photos.Count} uploaded successfully: {photo.FullPath}");
                }
                else {
                    Debug.WriteLine($"[BULK UPLOAD] Photo {i + 1}/{photos.Count} failed to upload: {photo.FullPath}");
                }
                alreadyUploadedCount++;
                // Final callback for file
                photoProgressCallback?.Invoke(
                    sessionUploadCount,
                    alreadyUploadedCount,
                    currentFile,
                    fileSize,
                    fileSize,
                    sessionUploadedBytes,
                    sessionTotalBytes
                );
                //MainPage._page.UpdatePhotoIsUploadedIndicator(photo);
                //invoke photoupdateisuploadedindicator on main UI thread
                System.Windows.Deployment.Current.Dispatcher.BeginInvoke(() => {
                    MainPage._page.UpdatePhotoIsUploadedIndicator(photo);
                });

            } catch (Exception ex) {
                Debug.WriteLine("Error uploading photo\n" + ex.ToString());
            }
        }
        return true;
    }

    public static async Task<bool> UploadPhotoAsync(PhotoItem photo, Action<long, long> progressCallback = null, bool skipNetworkTest = false) {
        if (photo == null) return false;
        Debug.WriteLine("[UPLOAD] Starting upload for photo: " + photo.FullPath);
        if(skipNetworkTest == false) {
            // 1. Ping server
            bool pingOk = false;
            try { pingOk = await TestConnectionAsync(); } catch { }
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
        }

        // 3. Load file
        string filePath = !string.IsNullOrEmpty(photo.FullPathHR) ? photo.FullPathHR : photo.FullPath;
        if (string.IsNullOrEmpty(filePath)) {
            Debug.WriteLine("[UPLOAD] File path is empty");
            return false;
        }

        Windows.Storage.StorageFile file = null;
        try { file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath); }
        catch {
            Debug.WriteLine("[UPLOAD] File not found: " + filePath);
            return false;
        }

        string url = GetServerIP() + "/api/assets";
        string key = _key;

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

        long totalBytes = ms.Length;

        // 5. SEND USING WINRT HTTPCLIENT WITH PROGRESS
        var filter = new Windows.Web.Http.Filters.HttpBaseProtocolFilter();
        var client = new Windows.Web.Http.HttpClient(filter);

        client.DefaultRequestHeaders.Authorization =
            new Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", key);

        var request = new Windows.Web.Http.HttpRequestMessage(
            Windows.Web.Http.HttpMethod.Post,
            new Uri(url)
        );

        var streamContent = new Windows.Web.Http.HttpStreamContent(ms.AsInputStream());
        streamContent.Headers.ContentType =
            new Windows.Web.Http.Headers.HttpMediaTypeHeaderValue("multipart/form-data");
        streamContent.Headers.ContentType.Parameters.Add(
            new Windows.Web.Http.Headers.HttpNameValueHeaderValue("boundary", boundary)
        );

        request.Content = streamContent;

        Debug.WriteLine("[UPLOAD] Sending upload request via WinRT HttpClient...");

        Windows.Web.Http.HttpResponseMessage response = null;

        try {
            var task = client.SendRequestAsync(request).AsTask(
                new Progress<HttpProgress>((p) => {
                    long sent = (long)p.BytesSent;
                    progressCallback?.Invoke(sent, totalBytes);
                    //Debug.WriteLine($"[UPLOAD PROGRESS] {sent}/{totalBytes} bytes");
                })
            );

            response = await task;
        }
        catch (Exception ex) {
            Debug.WriteLine("[UPLOAD ERROR] Exception during POST: " + ex);
            return false;
        }
        Debug.WriteLine("[UPLOAD] Request sent");

        string respText = await response.Content.ReadAsStringAsync();
        Debug.WriteLine("[UPLOAD RESPONSE] " + respText);

        if (respText.Contains("\"status\":\"created\"") ||
            respText.Contains("\"status\":\"duplicate\"") ||
            respText.Contains("\"status\":\"replaced\"")) {
            photo.IsUploaded = true;
            Debug.WriteLine("[UPLOAD] Upload successful");
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
