using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;
using System.Windows.Media.Imaging;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Lummich.Models {

    // ============================================================
    //  MEDIA TYPE
    // ============================================================
    public enum MediaType {
        Photo,
        Video
    }

    // ============================================================
    //  PHOTO ITEM – jedna položka (foto nebo video)
    // ============================================================
    public class PhotoItem {
        public string Id { get; set; } // SHA256 hash of FullPath
        public string FileHash { get; set; } // SHA256 hash of file contents. Used for Device Asset ID in API
        public MediaType Type { get; set; }

        public string FullPath { get; set; }
        public string FullPathHR { get; set; }
        public string ThumbPath { get; set; }

        public int Width { get; set; }
        public int Height { get; set; }

        public DateTime DateTaken { get; set; } //LOKALNI ČAS!!!

        public int LengthSeconds { get; set; }
        public bool IsFavorite { get; set; } = false;
        
        public bool IsUploaded { get; set; } = false;

    }

    // ============================================================
    //  PHOTO CACHE – generování a ukládání thumbnailů
    // ============================================================
    public static class PhotoCache {

        public static async Task<string> GetOrCreateThumbnailAsync(StorageFile file, MediaType type, int thumbSize = 24, int quality = 75)
        {
            string cacheFolder = $"thumbs/{thumbSize}";
            string hash = ComputeSHA256(file.Path);
            string thumbFileName = hash + ".png";

            using (var iso = IsolatedStorageFile.GetUserStoreForApplication())
            {
                if (!iso.DirectoryExists(cacheFolder))
                {
                    Debug("Creating thumbnail folder: " + cacheFolder);
                    iso.CreateDirectory(cacheFolder);
                }

                string fullThumbPath = Path.Combine(cacheFolder, thumbFileName);

                if (iso.FileExists(fullThumbPath))
                {
                    Debug("Thumbnail exists: " + fullThumbPath);
                    return fullThumbPath;
                }

                Debug($"Generating thumbnail for: {file.Name} size {thumbSize}");

                try
                {
                    if (type == MediaType.Photo)
                    {
                        await CreatePhotoThumbnail(file, fullThumbPath, iso, thumbSize);
                    }
                    else
                    {
                        await CreateVideoThumbnail(file, fullThumbPath, iso, thumbSize);
                    }
                }
                catch (Exception ex)
                {
                    Debug("Thumbnail generation FAILED: " + ex.Message);
                    return null;
                }

                Debug("Thumbnail saved: " + fullThumbPath);
                return fullThumbPath;
            }
        }

        private static async Task CreatePhotoThumbnail(StorageFile file, string path, IsolatedStorageFile iso, int thumbSize, int quality = 90) {
            Debug($"  [PHOTO] Creating thumbnail {thumbSize}x{thumbSize}...");

            using (var ras = await file.OpenReadAsync())
            using (var netStream = ras.AsStreamForRead()) {

                BitmapImage bmp = new BitmapImage();
                bmp.DecodePixelWidth = thumbSize;
                bmp.SetSource(netStream);

                WriteableBitmap wb = new WriteableBitmap(bmp);

                using (var isoStream = iso.CreateFile(path)) {
                    wb.SaveJpeg(isoStream, thumbSize, thumbSize, 0, quality);
                }
            }
        }

        private static async Task CreateVideoThumbnail(StorageFile file, string path, IsolatedStorageFile iso, int thumbSize, int quality = 90) {
            Debug($"  [VIDEO] Extracting first frame, size {thumbSize}x{thumbSize}...");
            try{
                var thumb = await file.GetThumbnailAsync(ThumbnailMode.VideosView);

                using (var ras = thumb)
                using (var netStream = ras.AsStreamForRead()) {

                    BitmapImage bmp = new BitmapImage();
                    bmp.DecodePixelWidth = thumbSize;
                    bmp.SetSource(netStream);

                    WriteableBitmap wb = new WriteableBitmap(bmp);

                    using (var isoStream = iso.CreateFile(path)) {
                        wb.SaveJpeg(isoStream, thumbSize, thumbSize, 0, quality);
                    }
                }
            } catch (Exception ex) {
                Debug("  [VIDEO] Thumbnail extraction FAILED: " + ex.Message);
            }
        }

        public static async Task<string> ComputeFileSHA256Async(StorageFile file){
            System.Diagnostics.Debug.WriteLine("Hashovani souboru: " + file.Path);
            using (var stream = await file.OpenStreamForReadAsync()) {
                using (var sha = new System.Security.Cryptography.SHA256Managed()) {
                    var hash = sha.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
            }
        }

        public static string ComputeSHA256(string input){
            System.Diagnostics.Debug.WriteLine("Hashovani stringu: " + input);
            using (var sha = new System.Security.Cryptography.SHA256Managed()) {
                var bytes = System.Text.Encoding.UTF8.GetBytes(input);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private static void Debug(string msg) {
            System.Diagnostics.Debug.WriteLine("[CACHE] " + msg);
        }
    }


    // ============================================================
    //  PHOTO SCANNER – hlavní logika
    // ============================================================
    public static class PhotoScanner {

        private const string ScannedListFile = "ScannedPhotos.xml";

        public static async Task SaveScannedListAsync(List<PhotoItem> items)
        {
            using (var iso = IsolatedStorageFile.GetUserStoreForApplication())
            {
                using (var stream = iso.OpenFile(ScannedListFile, FileMode.Create, FileAccess.Write))
                {
                    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(List<PhotoItem>));
                    serializer.Serialize(stream, items);
                }
            }
        }

        public static async Task<List<PhotoItem>> LoadScannedListAsync()
        {
            using (var iso = IsolatedStorageFile.GetUserStoreForApplication())
            {
                if (!iso.FileExists(ScannedListFile))
                    return new List<PhotoItem>();
                using (var stream = iso.OpenFile(ScannedListFile, FileMode.Open, FileAccess.Read))
                {
                    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(List<PhotoItem>));
                    return (List<PhotoItem>)serializer.Deserialize(stream);
                }
            }
        }

        public delegate void ScanProgressHandler(int current, int total, string filename);

        public static async Task<List<PhotoItem>> ScanAsync(List<string> folderNames, ScanProgressHandler progress = null)
        {
            // Load previously scanned list
            var scannedList = await LoadScannedListAsync();
            var scannedIds = new HashSet<string>(scannedList.Select(p => p.Id));
            var result = new List<PhotoItem>(scannedList); // Start with existing

            System.Diagnostics.Debug.WriteLine("=== SCAN START ===");
            System.Diagnostics.Debug.WriteLine("Folders: " + folderNames.Count);

            var pictures = await KnownFolders.PicturesLibrary.GetFoldersAsync();

            // Nejprve spočítáme celkový počet souborů (foto + video)
            int totalCount = 0;
            foreach (var folderName in folderNames)
            {
                var folder = pictures.FirstOrDefault(f => f.Name == folderName);
                if (folder == null) continue;

                var files = await folder.GetFilesAsync();
                totalCount += files.Count(f => IsImage(f.FileType) || IsVideo(f.FileType));
            }

            int currentIndex = 0;

            foreach (var folderName in folderNames)
            {
                System.Diagnostics.Debug.WriteLine("Scanning folder: " + folderName);

                var folder = pictures.FirstOrDefault(f => f.Name == folderName);
                if (folder == null)
                {
                    System.Diagnostics.Debug.WriteLine("  Folder not found!");
                    continue;
                }

                var files = await folder.GetFilesAsync();
                System.Diagnostics.Debug.WriteLine("  Files found: " + files.Count);

                var images = files.Where(f => IsImage(f.FileType)).ToList();
                var videos = files.Where(f => IsVideo(f.FileType)).ToList();

                System.Diagnostics.Debug.WriteLine("  Photos: " + images.Count);
                System.Diagnostics.Debug.WriteLine("  Videos: " + videos.Count);

                // ------------------------------------------
                // FOTO – deduplikace LQ/HR
                // ------------------------------------------
                var groups = images.GroupBy(f => GetBaseName(f.Name));

                foreach (var group in groups)
                {
                    StorageFile lq = null;
                    StorageFile hr = null;

                    foreach (var file in group)
                    {
                        if (file.Name.ToLower().Contains("__highres"))
                            hr = file;
                        else
                            lq = file;
                    }
                    StorageFile chosen = lq ?? hr;
                    currentIndex += group.Count();
                    progress?.Invoke(currentIndex, totalCount, chosen != null ? chosen.Name : "");
                    await Task.Yield(); // allow UI thread to update
                    if (chosen == null)
                        continue;

                    System.Diagnostics.Debug.WriteLine("  [PHOTO] " + chosen.Name);
                    // Compute hashId only once if not already in scannedIds
                    string hashId = PhotoCache.ComputeSHA256(chosen.Path);
                    PhotoItem existing = scannedList.FirstOrDefault(p => p.Id == hashId);
                    if (existing != null) {
                        // Already scanned, just add to result if not present
                        if (!result.Contains(existing)) {
                            result.Add(existing);
                            scannedIds.Add(hashId);
                        }
                        continue;
                    }
                    var props = await chosen.Properties.GetImagePropertiesAsync();
                    var localTime = ParseWpTimestamp(chosen, imgProps: props);
                    string thumb = await PhotoCache.GetOrCreateThumbnailAsync(chosen, MediaType.Photo);
                    // Ensure /24/ is in the path if missing (for backward compatibility)
                    if (!thumb.Contains("/24/") && !thumb.Contains("\\24\\")) {
                        thumb = System.IO.Path.Combine("thumbs/24", System.IO.Path.GetFileName(thumb));
                    }
                    string fileHash = await PhotoCache.ComputeFileSHA256Async(chosen);
                    var item = new PhotoItem
                    {
                        Id = hashId,
                        FileHash = fileHash,
                        Type = MediaType.Photo,
                        FullPath = chosen.Path,
                        FullPathHR = hr != null ? hr.Path : null,
                        ThumbPath = thumb,
                        Width = (int)props.Width,
                        Height = (int)props.Height,
                        DateTaken = localTime,
                        LengthSeconds = 0
                    };
                    result.Add(item);
                    scannedIds.Add(hashId);
                }

                // ------------------------------------------
                // VIDEO
                // ------------------------------------------
                foreach (var file in videos)
                {
                    currentIndex++;
                    progress?.Invoke(currentIndex, totalCount, file.Name);
                    await Task.Yield(); // allow UI thread to update

                    System.Diagnostics.Debug.WriteLine("  [VIDEO] " + file.Name);
                    string hashId = PhotoCache.ComputeSHA256(file.Path);
                    PhotoItem existing = scannedList.FirstOrDefault(p => p.Id == hashId);
                    if (existing != null) {
                        if (!result.Contains(existing)) {
                            result.Add(existing);
                            scannedIds.Add(hashId);
                        }
                        continue;
                    }
                    var props = await file.Properties.GetVideoPropertiesAsync();
                    var localTime = ParseWpTimestamp(file, vidProps: props);
                    string thumb = await PhotoCache.GetOrCreateThumbnailAsync(file, MediaType.Video);
                    if (!thumb.Contains("/24/") && !thumb.Contains("\\24\\")) {
                        thumb = System.IO.Path.Combine("thumbs/24", System.IO.Path.GetFileName(thumb));
                    }
                    string fileHash = await PhotoCache.ComputeFileSHA256Async(file);
                    var item = new PhotoItem
                    {
                        Id = hashId,
                        FileHash = fileHash,
                        Type = MediaType.Video,
                        FullPath = file.Path,
                        FullPathHR = null,
                        ThumbPath = thumb,
                        Width = (int)props.Width,
                        Height = (int)props.Height,
                        DateTaken = localTime,
                        LengthSeconds = (int)props.Duration.TotalSeconds
                    };
                    result.Add(item);
                    scannedIds.Add(hashId);
                }
            }

            System.Diagnostics.Debug.WriteLine("=== SCAN DONE ===");
            System.Diagnostics.Debug.WriteLine("Total items: " + result.Count);

            // Save updated scanned list
            await SaveScannedListAsync(result);

            return result;
        }



        // ============================================================
        //  Helpery
        // ============================================================

        private static DateTime ParseWpTimestamp(StorageFile file, ImageProperties imgProps = null, VideoProperties vidProps = null)
        {
            string name = file.Name;

            // 1) Match: WP_20260129_21_06_05_Pro.jpg
            var r1 = Regex.Match(name, @"(\d{8})_(\d{2})_(\d{2})_(\d{2})");
            if (r1.Success)
            {
                string date = r1.Groups[1].Value; // YYYYMMDD
                int year = int.Parse(date.Substring(0, 4));
                int month = int.Parse(date.Substring(4, 2));
                int day = int.Parse(date.Substring(6, 2));

                int hour = int.Parse(r1.Groups[2].Value);
                int minute = int.Parse(r1.Groups[3].Value);
                int second = int.Parse(r1.Groups[4].Value);

                return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
            }

            // 2) Match: WP_20260129_210605_Pro.jpg
            var r2 = Regex.Match(name, @"(\d{8})_(\d{6})");
            if (r2.Success)
            {
                string date = r2.Groups[1].Value; // YYYYMMDD
                string time = r2.Groups[2].Value; // HHMMSS

                int year = int.Parse(date.Substring(0, 4));
                int month = int.Parse(date.Substring(4, 2));
                int day = int.Parse(date.Substring(6, 2));

                int hour = int.Parse(time.Substring(0, 2));
                int minute = int.Parse(time.Substring(2, 2));
                int second = int.Parse(time.Substring(4, 2));

                return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
            }

            // 3) Fallback: EXIF (fotky)
            if (imgProps != null && imgProps.DateTaken != DateTimeOffset.MinValue)
                return imgProps.DateTaken.LocalDateTime;

            // 4) Fallback: DateCreated (videa)
            if (vidProps != null)
                return file.DateCreated.LocalDateTime;

            // 5) Ultimate fallback
            return file.DateCreated.LocalDateTime;
        }

        private static bool IsImage(string ext) {
            ext = ext.ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png";
        }

        private static bool IsVideo(string ext) {
            ext = ext.ToLower();
            return ext == ".mp4" || ext == ".wmv" || ext == ".avi";
        }

        private static string GetBaseName(string name) {
            if (name.ToLower().Contains("__highres"))
                return name.ToLower().Replace("__highres", "");
            return name.ToLower();
        }
    }
}
