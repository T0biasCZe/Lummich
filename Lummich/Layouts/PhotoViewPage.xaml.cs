using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Lummich.Models;
using System.Windows.Media.Imaging;
using System.IO.IsolatedStorage;
using System.IO;
using System.Windows.Input;
using System.Diagnostics;

namespace Lummich.Layouts {
    public partial class PhotoViewPage : PhoneApplicationPage {

        private PhotoItem _photo;
        private int _index;

        private double _currentScale = 1.0;
        private double _maxScale = 4.0;
        private bool _isDragging = false;

        public PhotoViewPage() {
            InitializeComponent();

            FullImage.Tap += FullImage_Tap;
            FullImage.DoubleTap += FullImage_DoubleTap;
        }

        // -----------------------------
        //   NAVIGACE
        // -----------------------------
        protected override void OnNavigatedTo(NavigationEventArgs e) {
            base.OnNavigatedTo(e);

            FullImage.Source = null;
            GC.Collect();

            string idStr;
            if (NavigationContext.QueryString.TryGetValue("id", out idStr)) {

                // Use string id directly
                _index = AppState.Photos.FindIndex(p => p.Id == idStr);
                _photo = AppState.Photos[_index];

                LoadPhoto(_photo);
            }
        }
        private async void LoadPhoto(PhotoItem p) {
            DateText.Text = p.DateTaken.ToString("dd.MM.yyyy HH:mm") + LangHelper.GetString("loadingFullImage");
            var bmpThumb = new BitmapImage();
            if (!String.IsNullOrEmpty(_photo.ThumbPath)) {
                Debug.WriteLine("Loading thumbnail from IS");
                using (var isoStore = IsolatedStorageFile.GetUserStoreForApplication()) {
                    using (var stream = isoStore.OpenFile(_photo.ThumbPath, FileMode.Open, FileAccess.Read)) {
                        bmpThumb.SetSource(stream);
                    }
                }
                FullImage.Source = bmpThumb;
                // Force UI refresh
                Dispatcher.BeginInvoke(() => { });
                Dispatcher.BeginInvoke(() => { });
                Dispatcher.BeginInvoke(() => { });
            }

            string path = p.FullPathHR ?? p.FullPath;
            var bmp = new BitmapImage();

            try {
                bool isVideo = p.Type == MediaType.Video;

                if (path.StartsWith("C:\\Data\\Users\\Public\\Pictures")) {
                    var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                    if (isVideo) {
                        var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.VideosView);
                        bmp.SetSource(thumb.AsStreamForRead());
                    } else {
                        using (var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read)) {
                            bmp.SetSource(stream.AsStreamForRead());
                        }
                    }
                }
                else {
                    if (isVideo) {
                        // Pro lokální video soubory bez WinRT thumbnail API zobrazit placeholder nebo nic
                        // bmp.UriSource = new Uri("/Assets/video_placeholder.png", UriKind.Relative);
                        //throw new Exception("Nelze načíst video soubor jako obrázek");
                        MessageBox.Show("Nelze načíst video soubor jako obrazek");
                    } else {
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read)) {
                            bmp.SetSource(fs);
                        }
                    }
                }
            }
            catch (Exception ex) {


                MessageBox.Show("Nelze načíst obrázek:\n"+ path.Replace("C:\\Data\\Users\\Public\\", "")  +"\n" + ex.Message);
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
            FullImage.Source = null;
            bmpThumb = null;
            GC.Collect();
            FullImage.Source = bmp;
            DateText.Text = p.DateTaken.ToString("dd.MM.yyyy HH:mm");

            double screenWidth = Application.Current.Host.Content.ActualWidth;
            _maxScale = Math.Max(4, bmp.PixelWidth * 4 / (screenWidth));
            Debug.WriteLine($"Screen width: {screenWidth}, Image width: {bmp.PixelWidth}, Max scale: {_maxScale}");
            ResetZoom();

            ResetZoom();
        }


        private void ZoomViewer_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e) {

            _currentScale = ImageTransform.ScaleX;

            // SWIPE LEFT / RIGHT (jen když není zoom)
            if (!_isDragging && _currentScale == 1) {

                if (e.FinalVelocities.LinearVelocity.X < -500)
                    NextPhoto();

                else if (e.FinalVelocities.LinearVelocity.X > 500)
                    PrevPhoto();
            }

            _isDragging = false;
        }

        // -----------------------------
        //   DOUBLE TAP ZOOM
        // -----------------------------
        private void FullImage_DoubleTap(object sender, System.Windows.Input.GestureEventArgs e) {
            var pos = e.GetPosition(FullImage);

            if (_currentScale < 2)
                SetZoom(_maxScale, pos);
            else
                SetZoom(1, new Point(FullImage.ActualWidth / 2, FullImage.ActualHeight / 2));
        }


        private void SetZoom(double scale, Point center) {

            ImageTransform.CenterX = center.X;
            ImageTransform.CenterY = center.Y;

            ImageTransform.ScaleX = scale;
            ImageTransform.ScaleY = scale;

            _currentScale = scale;
        }

        // -----------------------------
        //   FULLSCREEN TOGGLE
        // -----------------------------
        private void FullImage_Tap(object sender, System.Windows.Input.GestureEventArgs e) {
            bool visible = TopBar.Visibility == Visibility.Visible;

            TopBar.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            BottomBar.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        }


        // -----------------------------
        //   DALŠÍ / PŘEDCHOZÍ FOTO
        // -----------------------------
        private void NextPhoto() {
            if (_index < AppState.Photos.Count - 1) {
                _index++;
                _photo = AppState.Photos[_index];
                ResetZoom();
                LoadPhoto(_photo);
            }
        }

        private void PrevPhoto() {
            if (_index > 0) {
                _index--;
                _photo = AppState.Photos[_index];
                ResetZoom();
                LoadPhoto(_photo);
            }
        }

        private void ResetZoom() {
            _currentScale = 1;
            ImageTransform.ScaleX = 1;
            ImageTransform.ScaleY = 1;
            ImageTransform.TranslateX = 0;
            ImageTransform.TranslateY = 0;
        }

        // -----------------------------
        //   HW BACK TLACITKO
        // -----------------------------
        protected override void OnBackKeyPress(System.ComponentModel.CancelEventArgs e) {
            if (_currentScale > 1) {
                ResetZoom();
                e.Cancel = true;
                return;
            }

            base.OnBackKeyPress(e);
        }

        private void Zoom_ManipulationDelta(object sender, ManipulationDeltaEventArgs e) {
            // PINCH
            if (e.PinchManipulation != null) {
                var pinch = e.PinchManipulation;

                double scale = _currentScale * pinch.CumulativeScale;
                scale = Math.Max(1, Math.Min(_maxScale, scale));

                if (scale >= 3) {
                    // Zoom around current visible center of the image
                    ImageTransform.CenterX = FullImage.ActualWidth / 2 - ImageTransform.TranslateX;
                    ImageTransform.CenterY = FullImage.ActualHeight / 2 - ImageTransform.TranslateY;
                } else {
                    ImageTransform.CenterX = pinch.Original.Center.X;
                    ImageTransform.CenterY = pinch.Original.Center.Y;
                }

                ImageTransform.ScaleX = scale;
                ImageTransform.ScaleY = scale;

                _currentScale = scale;
                return;
            }

            // DRAG
            if (_currentScale > 1) {
                ImageTransform.TranslateX += e.DeltaManipulation.Translation.X * _currentScale;
                ImageTransform.TranslateY += e.DeltaManipulation.Translation.Y * _currentScale;
                _isDragging = true;
            }
        }


        private void Zoom_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e) {
            _currentScale = ImageTransform.ScaleX;

            // SWIPE LEFT / RIGHT (jen když není zoom)
            if (!_isDragging && _currentScale == 1) {
                if (e.FinalVelocities.LinearVelocity.X < -500)
                    NextPhoto();
                else if (e.FinalVelocities.LinearVelocity.X > 500)
                    PrevPhoto();
            }

            // SWIPE DOWN → zavřít
            if (_currentScale == 1 && e.FinalVelocities.LinearVelocity.Y > 800) {
                NavigationService.GoBack();
                return;
            }

            _isDragging = false;
        }
        public static string FormatSize(long bytes) {
            double value = bytes;
            string unit = "B";

            if (value >= 1024) {
                value /= 1024;
                unit = "KiB";
            }
            if (value >= 1024) {
                value /= 1024;
                unit = "MiB";
            }
            if (value >= 1024) {
                value /= 1024;
                unit = "GiB";
            }
            if (value >= 1024) {
                value /= 1024;
                unit = "TiB";
            }

            // 3 číslice:
            // 1.21KiB
            // 12.1KiB
            // 121KiB
            if (value < 10) return value.ToString("0.00") + unit;   // 1.23KiB
            else if (value < 100) return value.ToString("0.0") + unit;    // 12.3KiB
            else return value.ToString("0") + unit;      // 123KiB
        }

        private void UpdateUploadUI(long sent, long total) {
            Deployment.Current.Dispatcher.BeginInvoke(() => {
                double percent = (total > 0) ? (sent * 100.0 / total) : 0;

                string sentStr = FormatSize(sent);
                string totalStr = FormatSize(total);

                UploadProgressText.Text = $"{percent:0}%  {sentStr} / {totalStr}";

                UploadProgressBar.Maximum = total;
                UploadProgressBar.Value = sent;
            });
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e) {
            UploadButtonImage.Opacity = 0.25;
            UploadButton.IsEnabled = false;

            UploadProgressBar.Value = 0;
            UploadProgressText.Text = "0% 0.00KiB / 0.00MiB";
            UploadProgressBar.Visibility = Visibility.Visible;
            UploadProgressText.Visibility = Visibility.Visible;
            await ImmichApi.UploadPhotoAsync(_photo, (sent, total) => { UpdateUploadUI(sent, total); });
            await PhotoScanner.SaveScannedListAsync(AppState.Photos);
            if (MainPage._page != null) {
                MainPage._page.UpdatePhotoIsUploadedIndicator(_photo);
            } else {
                Debug.WriteLine("Cant refresh grid upload indicator, _page null");
            }
            UploadButtonImage.Opacity = 1;
            UploadButton.IsEnabled = true;
            UploadProgressBar.Visibility = Visibility.Collapsed;
            UploadProgressText.Visibility = Visibility.Collapsed;
        }

        private void PhoneApplicationPage_Loaded(object sender, RoutedEventArgs e) {
            LangHelper.TranslatePage(this, "PhotoViewPage");
        }
    }
}
