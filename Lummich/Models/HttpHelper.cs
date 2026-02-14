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
        try {
            var client = CreateHttpClient();
            Debug.WriteLine("[HTTP Async GET] Sending JSON request to URL " + url);
            var content = new HttpStringContent(json, Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
            var response = await client.PostAsync(new Uri(url), content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) {
            Debug.WriteLine("[HTTP ASYNC POST] HttpClient failed: " + ex.ToString());
        }
        return "";
    }

    public static async Task<string> GetAsync(string url, string key = null) {
        try {
            var client = CreateHttpClient();
            if(key != null) {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);
            }
            Debug.WriteLine("[HTTP Async GET] Sending request to URL " + url);
            var response = await client.GetAsync(new Uri(url));

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex) {
            Debug.WriteLine("[HttpHelper.GetAsync] HttpClient failed: " + ex.ToString());
        }
        return "";
    }
}
