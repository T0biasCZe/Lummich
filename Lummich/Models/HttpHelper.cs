using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Web.Http;
using Windows.Web.Http.Filters;
using Windows.Security.Cryptography.Certificates;
using System.Diagnostics;


public static class HttpHelper {
    public static bool IgnoreSSLErrors = false;

    private static HttpClient CreateHttpClient() {
        if (IgnoreSSLErrors) {
            var filter = new HttpBaseProtocolFilter();
            filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Untrusted);
            filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.InvalidName);
            filter.IgnorableServerCertificateErrors.Add(ChainValidationResult.Expired);
            return new HttpClient(filter);
        } else {
            return new HttpClient();
        }
    }

    public static async Task<string> PostJsonAsync(string url, string json) {
        var client = CreateHttpClient();
        var content = new HttpStringContent(json, Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
        var response = await client.PostAsync(new Uri(url), content);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public static async Task<string> GetAsync(string url) {
        try {
            var client = CreateHttpClient();
            var response = await client.GetAsync(new Uri(url));
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) {
            Debug.WriteLine("[HttpHelper.GetAsync] HttpClient failed: " + ex.ToString());
            // Fallback: try HttpWebRequest (Silverlight/WinRT compatibility)
            try {
                var tcs = new TaskCompletionSource<string>();
                var request = System.Net.HttpWebRequest.Create(url) as System.Net.HttpWebRequest;
                request.Method = "GET";
                request.BeginGetResponse(ar => {
                    try {
                        var resp = request.EndGetResponse(ar) as System.Net.HttpWebResponse;
                        using (var stream = resp.GetResponseStream())
                        using (var reader = new System.IO.StreamReader(stream))
                        {
                            string result = reader.ReadToEnd();
                            tcs.SetResult(result);
                        }
                    } catch (Exception ex2) {
                        Debug.WriteLine("[HttpHelper.GetAsync] HttpWebRequest failed: " + ex2.ToString());
                        tcs.SetException(ex2);
                    }
                }, null);
                return await tcs.Task;
            }
            catch (Exception ex2) {
                Debug.WriteLine("[HttpHelper.GetAsync] HttpWebRequest outer failed: " + ex2.ToString());
                throw;
            }
        }
    }
}
