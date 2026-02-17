using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Shell;
using System.Windows.Media.Imaging;
using Windows.Storage;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.Xna.Framework.Media;
using System.Collections.ObjectModel;
using Lummich.Models;
using System.IO.IsolatedStorage;
using System.IO;
using System.Diagnostics;
using Microsoft.Phone.Info;
using System.Windows.Threading;
using System.Windows.Input;
using Lummich.Layouts;

namespace Lummich {
	public partial class MainPage : PhoneApplicationPage {
		public static MainPage _page = null;

		private int photosPerRow = 4;
		private int cellSize = 40;
		private List<string> selectedFolders = new List<string>();
		private ObservableCollection<string> availableFolders = new ObservableCollection<string>();
		private ScrollViewer _scroll;
		private List<Grid> _cells = new List<Grid>();
		private double _lastOffset = 0;
		private Queue<Grid> _loadQueue = new Queue<Grid>();
		private int _activeLoads = 0;
		private const int MAX_ACTIVE_LOADS = 1;
		private const int MAX_LOADED_IMAGES = 60;

		private Dictionary<Grid, bool> _isLoaded = new Dictionary<Grid, bool>();
		private Dictionary<Grid, DateTime> _lastVisible = new Dictionary<Grid, DateTime>();
        private bool _isAtTop = false;
        private DispatcherTimer _rescanTimer;




        public MainPage() {
			System.Diagnostics.Debug.WriteLine("Initialize component");

			InitializeComponent();

			_page = this;

			// Připojit handler pro načtení server settings UI
			if (ServerSettingsPanel != null)
				ServerSettingsPanel.Loaded += ServerSettingsPanel_Loaded;

            // --- AUTOSAVE pro server settings ---
            if (GlobalDomainBox != null) {
                GlobalDomainBox.TextChanged += ServerSettings_AutoSave;
            }
            if (LocalIpBox != null) {
                LocalIpBox.TextChanged += ServerSettings_AutoSave;
            }
            if(EmailBox != null) {
                EmailBox.TextChanged += ServerSettings_AutoSave;
            }
            if (PasswordBox != null) {
                PasswordBox.PasswordChanged += ServerSettings_AutoSave;
            }
            if (PasswordTextBox != null) {
                PasswordTextBox.TextChanged += ServerSettings_AutoSave;
            }


            System.Diagnostics.Debug.WriteLine("Loading photos per low application settings");
			// Load photosPerRow from settings
			if (IsolatedStorageSettings.ApplicationSettings.Contains("PhotosPerRow"))
			{
				object val = IsolatedStorageSettings.ApplicationSettings["PhotosPerRow"];
				int n;
				if (val != null && int.TryParse(val.ToString(), out n) && n >= 2 && n <= 6)
					photosPerRow = n;
			}
			if (PhotosPerRowBox != null)
				PhotosPerRowBox.Text = photosPerRow.ToString();


			System.Diagnostics.Debug.WriteLine("Loading folders");

			selectedFolders = FolderManager.LoadFolders();
			FolderList.ItemsSource = selectedFolders;
			FolderPickerList.ItemsSource = availableFolders;

			this.Loaded += MainPage_Loaded;

			if (ShowPasswordCheckBox != null)
				ShowPasswordCheckBox.IsChecked = false;

            string SSID;
            var icp = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (icp != null) {
                Debug.WriteLine("ICP not null");
                if (icp.WlanConnectionProfileDetails != null) {
                    Debug.WriteLine("Wlan details not null"); 
                    SSID = icp.WlanConnectionProfileDetails.GetConnectedSsid();
                    Debug.WriteLine("SSID: " + SSID);
                }
            }
            else {
                Debug.WriteLine("ICP null");
            }

            uploadedIndicator =  new BitmapImage(new Uri("/Assets/yes.png", UriKind.Relative));
            notUploadedIndicator = new BitmapImage(new Uri("/Assets/no.png", UriKind.Relative));
            System.Diagnostics.Debug.WriteLine("MainPage constructor done");
        }
		private async void DeleteScanCacheButton_Click(object sender, RoutedEventArgs e) {
			string title = LangHelper.GetString("deleteScanCacheTitle");
			string message = LangHelper.GetString("deleteScanCacheMessage");
			string yes = LangHelper.GetString("yes");
			string no = LangHelper.GetString("no");

			var result = MessageBox.Show(message, title, MessageBoxButton.OKCancel);
			if (result == MessageBoxResult.OK) {
				AppState.Photos = new List<PhotoItem>();
				await PhotoScanner.SaveScannedListAsync(AppState.Photos);
				MessageBox.Show(LangHelper.GetString("deleteScanCacheDone"));
			}
		}

		private void ShowPasswordCheckBox_Checked(object sender, RoutedEventArgs e)
		{
			if (PasswordBox == null || PasswordTextBox == null) return;
			PasswordTextBox.Text = PasswordBox.Password;
			PasswordTextBox.Visibility = Visibility.Visible;
			PasswordBox.Visibility = Visibility.Collapsed;
		}

		private void ShowPasswordCheckBox_Unchecked(object sender, RoutedEventArgs e)
		{
			if (PasswordBox == null || PasswordTextBox == null) return;
			PasswordBox.Password = PasswordTextBox.Text;
			PasswordBox.Visibility = Visibility.Visible;
			PasswordTextBox.Visibility = Visibility.Collapsed;
		}
        bool rescanRunning = false;

        private async Task RescanPhotos() {
            if (rescanRunning) {
                Debug.WriteLine("rescan already running!");
                return;
            }
            rescanRunning = true;

            ScanProgressPanel.Visibility = Visibility.Visible;
            ScanProgressText.Text = LangHelper.GetString("scanning");
            ScanCurrentFileText.Text = "";
            ScanProgressBar.Value = 0;

            ScanProgressPanel.Visibility = Visibility.Visible;
			AppState.Photos = await PhotoScanner.ScanAsync(selectedFolders, OnScanProgress);

			if(AppState.Photos == null || AppState.Photos.Count == 0) {
				RefreshButton.Visibility = Visibility.Visible;
                NoMediaText.Visibility = Visibility.Visible;
				PhotoScroll.Visibility = Visibility.Collapsed;
				PhotoSectionsPanel.Visibility = Visibility.Collapsed;
            }
			else {
				RefreshButton.Visibility = Visibility.Collapsed;
                NoMediaText.Visibility = Visibility.Collapsed;
				PhotoScroll.Visibility = Visibility.Visible;
				PhotoSectionsPanel.Visibility = Visibility.Visible;
            }

			ScanProgressBar.Value = 0;
			ScanProgressText.Text = LangHelper.GetString("loadinggrid");
			ScanCurrentFileText.Text = "";

			await PopulateGridWithProgress(AppState.Photos);
            rescanRunning = false;
            CheckVisibleCells();
        }
        private async void MainPage_Loaded(object sender, RoutedEventArgs e) {

            System.Diagnostics.Debug.WriteLine("Main page loaded");
            
            LangHelper.TranslatePage(this, "MainPage");

            System.Diagnostics.Debug.WriteLine("Starting async scan");

            if (AppState.Photos == null || AppState.Photos.Count == 0) {
                await RescanPhotos();
            }

            // 4) HOTOVO
            ScanProgressPanel.Visibility = Visibility.Collapsed;

            _scroll = FindScrollViewer(this);

            if (_scroll != null) {
                System.Diagnostics.Debug.WriteLine("[SCROLL] ScrollViewer FOUND");
                _lastOffset = _scroll.VerticalOffset;
                CompositionTarget.Rendering += OnRendering;

                // Attach manipulation events directly to ScrollViewer
                _scroll.ManipulationStarted += Scroll_ManipulationStarted;
                _scroll.ManipulationDelta += Scroll_ManipulationDelta;
                _scroll.ManipulationCompleted += Scroll_ManipulationCompleted;
            }
            else {
                System.Diagnostics.Debug.WriteLine("[SCROLL] ScrollViewer NOT FOUND!");
            }
            _rescanTimer = new DispatcherTimer();
            _rescanTimer.Interval = TimeSpan.FromSeconds(0.4);
            _rescanTimer.Tick += RescanTimer_Tick;

            // Touch.FrameReported fallback for pull-to-refresh
            Touch.FrameReported += Touch_FrameReported;
            // Fallback: Touch.FrameReported for pull-to-refresh

            RefreshButton.Click += RefreshButton_Click;
        }
		private void Touch_FrameReported(object sender, TouchFrameEventArgs e)
		{
			if (_scroll == null) return;
			if (_scroll.VerticalOffset > 0.1) return;

			// Always set _isAtTop if offset is at top (even if grid is empty)
			_isAtTop = true;

			// Use RootVisual for GetTouchPoints to avoid 'Value does not fall within the expected range.'
			var points = e.GetTouchPoints(Application.Current.RootVisual);
			if (points.Count == 0) return;

			// Detect upward drag at top
			foreach (var tp in points)
			{
				if (tp.Action == TouchAction.Move && tp.Position.Y < 100 && tp.Position.Y - tp.Position.Y < 0)
				{
					// User is dragging up at the top
					_rescanTimer.Stop();
					_rescanTimer.Start();
					break;
				}
			}
		}
        private void Scroll_ManipulationStarted(object sender, ManipulationStartedEventArgs e) {
            Debug.WriteLine("Manipulation štart");
            if (_scroll == null) return;
            _isAtTop = _scroll.VerticalOffset <= 0.1;
        }

        private void Scroll_ManipulationDelta(object sender, ManipulationDeltaEventArgs e) {
            if (_scroll == null) return;
			// Detect pull-to-refresh: if at top and user drags down by more than 4px
			Debug.WriteLine($"Manipulation delta {e.DeltaManipulation.Translation.Y}, offset {_scroll.VerticalOffset}");

			if (_scroll.VerticalOffset <= 0.1 && e.DeltaManipulation.Translation.Y > 4)
			{
				Debug.WriteLine("[PULL-TO-REFRESH] Triggered");
				_rescanTimer.Stop();
				_rescanTimer.Start();
			}
			else
			{
				_rescanTimer.Stop();
			}
        }

        private void Scroll_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e) {
            Debug.WriteLine("Manipulation completed");
            if(_rescanTimer != null) _rescanTimer.Stop();
        }

        private async void RescanTimer_Tick(object sender, EventArgs e) {
            _rescanTimer.Stop();
            Debug.WriteLine($"Rescan timer fire attop: {_isAtTop}");

            if (_isAtTop) {
                System.Diagnostics.Debug.WriteLine("[RESCAN] Triggered by upward drag at top");
                await RescanPhotos();
                ScanProgressPanel.Visibility = Visibility.Collapsed;
            }
        }


        private void OnRendering(object sender, EventArgs e) {
			if (_scroll == null)
				return;

			double offset = _scroll.VerticalOffset;

			if (Math.Abs(offset - _lastOffset) > 0.5) {
				System.Diagnostics.Debug.WriteLine("[SCROLL] Offset changed: " + offset);
				_lastOffset = offset;
				CheckVisibleCells();
			}
		}
		// Lazy load progress state for current operation
		private int _currentVisibleCount = 0;
		private int _currentHQToLoad = 0;
		private int _currentHQLoaded = 0;

		private void UpdateLazyLoadProgressUI(string mode, string filename = "")
		{
			// mode = "load" nebo "unload"
			// _currentVisibleCount: kolik obrázků je aktuálně viditelných
			// _currentHQToLoad: kolik z nich je potřeba načíst HQ
			// _currentHQLoaded: kolik z nich už bylo HQ načteno během této operace

			ScanProgressBar.Maximum = _currentHQToLoad > 0 ? _currentHQToLoad : 1;
			ScanProgressBar.Value = _currentHQLoaded;

			if (mode == "load")
			{
				ScanProgressText.Text =
					LangHelper.GetString("loadingHQPhotos") + " " +
					_currentHQLoaded + "/" + _currentHQToLoad;
			}
			else if (mode == "unload")
			{
				ScanProgressText.Text =
					LangHelper.GetString("unloadingPhotos") + " " +
					_currentHQLoaded + "/" + _currentHQToLoad;
			}

			ScanCurrentFileText.Text = filename;

			// Panel se zobrazí jen když se něco děje
			if (_activeLoads > 0 || _loadQueue.Count > 0 || mode == "unload")
				ScanProgressPanel.Visibility = Visibility.Visible;
			else
				ScanProgressPanel.Visibility = Visibility.Collapsed;
		}


		private void CheckVisibleCells()
		{
			double screenHeight = Application.Current.Host.Content.ActualHeight;

			// Nové proměnné pro progress
			int visibleCount = 0;
			int hqToLoad = 0;
			int hqLoaded = 0;

			foreach (var cell in _cells)
			{
				if (cell == null)
				{
					System.Diagnostics.Debug.WriteLine("[ERROR] cell == null");
					continue;
				}

				if (cell.Tag == null)
				{
					System.Diagnostics.Debug.WriteLine("[ERROR] cell.Tag == null");
					continue;
				}

				var photo = cell.Tag as PhotoItem;
				if (photo == null)
				{
					System.Diagnostics.Debug.WriteLine("[ERROR] cell.Tag is not PhotoItem");
					continue;
				}

				if (cell.Children.Count == 0 || !(cell.Children[0] is Image))
				{
					System.Diagnostics.Debug.WriteLine("[ERROR] cell.Children[0] is not Image");
					continue;
				}

				// Transform
				Point pos;
				try
				{
					var transform = cell.TransformToVisual(Application.Current.RootVisual);
					pos = transform.Transform(new Point(0, 0));
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine("[ERROR] Transform failed: " + ex.Message);
					continue;
				}

				//bool visible = pos.Y >= 0 && pos.Y <= screenHeight - cellSize;
				//tolerance načtení až půl obrázku pod okrajem
				bool visible = pos.Y + cellSize * 0.5 >= 0 && pos.Y <= screenHeight - cellSize * 0.5;

				if (visible)
				{
					visibleCount++;
					_lastVisible[cell] = DateTime.Now;

					bool isHQ = _isLoaded.ContainsKey(cell) && _isLoaded[cell];
					if (!isHQ)
					{
						hqToLoad++;
						System.Diagnostics.Debug.WriteLine("[QUEUE] Enqueue " + photo.FullPath);
						EnqueueLoad(cell);
					}
					else
					{
						hqLoaded++;
					}
				}
			}

			// Nastavit progress pro aktuální operaci
			_currentVisibleCount = visibleCount;
			_currentHQToLoad = hqToLoad;
			_currentHQLoaded = 0; // reset při nové vlně načítání
			UpdateLazyLoadProgressUI("load");

			TryStartNextLoad();
			TryUnloadOld();
		}




		private void UnloadCell(Grid cell) {
			var img = cell.Children[0] as Image;
			var photo = (PhotoItem)cell.Tag;

			System.Diagnostics.Debug.WriteLine("[UNLOAD] " + photo.FullPath);
			img.Source = null;
			GC.Collect();

			var bmp = new BitmapImage();
			using (var iso = IsolatedStorageFile.GetUserStoreForApplication())
			using (var stream = iso.OpenFile(photo.ThumbPath, FileMode.Open, FileAccess.Read)) {
				bmp.SetSource(stream);
			}

			img.Source = bmp;
			_isLoaded[cell] = false;
		}

		private void TryUnloadOld() {
			int loadedCount = _isLoaded.Count(c => c.Value);

			if (loadedCount < MAX_LOADED_IMAGES)
				return;

			var now = DateTime.Now;

			foreach (var kv in _isLoaded.ToList()) {
				var cell = kv.Key;

				if (!kv.Value)
					continue;

				if (!_lastVisible.ContainsKey(cell))
					continue;

				if ((now - _lastVisible[cell]).TotalSeconds > 5) {
					var photo = (PhotoItem)cell.Tag;

					UpdateLazyLoadProgressUI(
						"unload",
						System.IO.Path.GetFileName(photo.FullPath)
					);

					UnloadCell(cell);
				}
			}

			UpdateLazyLoadProgressUI("unload");
		}


		private void EnqueueLoad(Grid cell) {
			if (!_loadQueue.Contains(cell)) {
				_loadQueue.Enqueue(cell);
				System.Diagnostics.Debug.WriteLine("[QUEUE] Added. Queue size = " + _loadQueue.Count);
			}
		}


		private async void TryStartNextLoad()
		{
			if (_activeLoads >= MAX_ACTIVE_LOADS)
				return;

			if (_loadQueue.Count == 0)
				return;

			var cell = _loadQueue.Dequeue();
			_activeLoads++;

			var photo = (PhotoItem)cell.Tag;

			System.Diagnostics.Debug.WriteLine("[LOAD] Start HR load: " + photo.FullPath);

			// Před načtením: progress ukazuje kolik už bylo načteno
			UpdateLazyLoadProgressUI("load", System.IO.Path.GetFileName(photo.FullPath));

			await LoadHighRes(cell);

			_activeLoads--;

			// Po načtení jednoho HQ obrázku, inkrementovat _currentHQLoaded
			_currentHQLoaded++;
			UpdateLazyLoadProgressUI("load");

			System.Diagnostics.Debug.WriteLine("[LOAD] Done HR load");
            await Task.Yield();

			TryStartNextLoad();
		}


		private async Task LoadHighRes(Grid cell) {
			var photo = (PhotoItem)cell.Tag;
			var img = cell.Children[0] as Image;

			try {
				// Always use PhotoCache for HQ thumb (works for both images and videos)
				var file = await StorageFile.GetFileFromPathAsync(photo.FullPath);
				string thumbPath = await Lummich.Models.PhotoCache.GetOrCreateThumbnailAsync(file, photo.Type, cellSize);
				if (!string.IsNullOrEmpty(thumbPath)) {
					BitmapImage bmp = new BitmapImage();
					using (var iso = IsolatedStorageFile.GetUserStoreForApplication())
					using (var stream = iso.OpenFile(thumbPath, FileMode.Open, FileAccess.Read)) {
						bmp.SetSource(stream);
					}
					img.Source = null;
					GC.Collect();
					img.Source = bmp;
					_isLoaded[cell] = true;
				} else {
					System.Diagnostics.Debug.WriteLine($"[LOAD] ERROR: Could not generate HQ thumb for {photo.FullPath}");
				}
			} catch (Exception ex) {
				System.Diagnostics.Debug.WriteLine("[LOAD] ERROR: " + ex.Message);
			}

			long used = DeviceStatus.ApplicationCurrentMemoryUsage;
			long limit = DeviceStatus.ApplicationMemoryUsageLimit;

			Debug.WriteLine("Used: " + used / 1024 / 1024 + " MiB from " + limit / 1024 / 1024 + " MiB");
		}

		private async Task PopulateGridWithProgress(List<PhotoItem> photos) {
			PhotoSectionsPanel.Children.Clear();
			_cells.Clear();
            GC.Collect();

			int total = photos.Count;
			int current = 0;

			// GROUPING podle roku/měsíce
			var grouped = photos
				.OrderByDescending(p => p.DateTaken)
				.GroupBy(p => new { p.DateTaken.Year, p.DateTaken.Month });

			foreach (var g in grouped) {
				// Nadpis
				var title = new TextBlock {
					Text = $"{LangHelper.GetString("month" + (g.Key.Month - 1))} {g.Key.Year}",
					FontSize = 32,
					FontWeight = FontWeights.Bold,
					Margin = new Thickness(0, 24, 0, 12)
				};

				PhotoSectionsPanel.Children.Add(title);

				// Grid pro fotky
				var grid = new Grid();
				int columns = photosPerRow;
                if (columns < 2) columns = 2;

				double itemWidth = (ActualWidth / columns) - 8;
				cellSize = (int)(itemWidth + 0.5);
				System.Diagnostics.Debug.WriteLine("Cell Size: " + cellSize);

				for (int i = 0; i < columns; i++)
					grid.ColumnDefinitions.Add(new ColumnDefinition());

				var list = g.ToList();

				for (int i = 0; i < list.Count; i++) {
					AddPhotoToGrid(list[i], i, columns, itemWidth, grid);

					// PROGRESS UPDATE
					current++;
					ScanProgressBar.Maximum = total;
					ScanProgressBar.Value = current;
					ScanProgressText.Text = $"{LangHelper.GetString("loaded")} {current} / {total}";
					ScanCurrentFileText.Text = list[i].ThumbPath;

					await Task.Yield(); // nech UI nadechnout
				}

				PhotoSectionsPanel.Children.Add(grid);
			}
		}



		private void OnScanProgress(int current, int total, string filename){
			Dispatcher.BeginInvoke(() => {
				ScanProgressPanel.Visibility = Visibility.Visible;

				double percent = (double)current / total * 100.0;
				ScanProgressBar.Value = percent;

				ScanProgressText.Text = $"Skenuji {current} / {total}";
				ScanCurrentFileText.Text = filename;
			});
		}

		private async void AddFolder_Click(object sender, RoutedEventArgs e) {
			if (FolderListPanel != null) FolderListPanel.Visibility = System.Windows.Visibility.Collapsed;
			if (FolderPickerPanel != null) FolderPickerPanel.Visibility = System.Windows.Visibility.Visible;
			await LoadAvailableFoldersAsync();
		}

		private async Task LoadAvailableFoldersAsync() {
			availableFolders.Clear();

			var folders = await GetAllPictureFoldersAsync();
			System.Diagnostics.Debug.WriteLine("Folders found: " + folders.Count);
			foreach (var f in folders) System.Diagnostics.Debug.WriteLine(f);
			foreach (var f in folders)
			{
				availableFolders.Add(f);
			}
		}

		// New async method to get all folders in the Pictures library using WinRT
		private async Task<List<string>> GetAllPictureFoldersAsync()
		{
			var result = new List<string>();
			try
			{
				var picturesLibrary = Windows.Storage.KnownFolders.PicturesLibrary;
				var folders = await picturesLibrary.GetFoldersAsync();
				foreach (var folder in folders)
				{
					result.Add(folder.Name);
				}
				if (folders.Count == 0)
				{
					System.Diagnostics.Debug.WriteLine("No folders found in PicturesLibrary.");
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine("Exception in GetAllPictureFoldersAsync: " + ex);
				result.Add("Error: " + ex.Message);
			}
			return result;
		}

		private async Task LoadFromFolder(StorageFolder root) {
			var folders = await root.GetFoldersAsync();

			foreach (var f in folders) {
				availableFolders.Add(f.Name);
			}
		}
		private void ConfirmFolderPicker_Click(object sender, RoutedEventArgs e) {
			var newList = new List<string>();

			foreach (var item in FolderPickerList.Items) {
				var container = FolderPickerList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
				var checkbox = FindCheckBox(container);

				if (checkbox != null && checkbox.IsChecked == true) {
					newList.Add(checkbox.Tag.ToString());
				}
			}

			selectedFolders = newList;
			FolderManager.SaveFolders(selectedFolders);

			FolderList.ItemsSource = null;
			FolderList.ItemsSource = selectedFolders;

			if (FolderListPanel != null) FolderListPanel.Visibility = System.Windows.Visibility.Visible;
			if (FolderPickerPanel != null) FolderPickerPanel.Visibility = System.Windows.Visibility.Collapsed;
		}
		private void CancelFolderPicker_Click(object sender, RoutedEventArgs e) {
			if (FolderListPanel != null) FolderListPanel.Visibility = System.Windows.Visibility.Visible;
			if (FolderPickerPanel != null) FolderPickerPanel.Visibility = System.Windows.Visibility.Collapsed;
		}
		private void RemoveFolder_Click(object sender, RoutedEventArgs e) {
			var btn = sender as Button;
			var folder = btn.Tag as string;

			selectedFolders.Remove(folder);
			FolderManager.SaveFolders(selectedFolders);

			FolderList.ItemsSource = null;
			FolderList.ItemsSource = selectedFolders;
		}
		private CheckBox FindCheckBox(DependencyObject parent) {
			if (parent == null) return null;

			var cb = parent as CheckBox;
			if (cb != null) return cb;

			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
				var child = VisualTreeHelper.GetChild(parent, i);
				var result = FindCheckBox(child);
				if (result != null) return result;
			}

			return null;
		}


		private void AddMonthSection(int year, int month, List<PhotoItem> items) {
			// Nadpis
			var title = new TextBlock {
				Text = $"{LangHelper.GetString("month" + (month - 1))} {year}",
				FontSize = 32,
				FontWeight = FontWeights.Bold,
				Margin = new Thickness(0, 24, 0, 12)
			};

			PhotoSectionsPanel.Children.Add(title);

			// Grid pro fotky
			var grid = new Grid();
			int columns = photosPerRow;
			double itemWidth = (ActualWidth / columns) - 8;

			// vytvořit sloupce
			for (int i = 0; i < columns; i++)
				grid.ColumnDefinitions.Add(new ColumnDefinition());

			// přidat fotky
			for (int i = 0; i < items.Count; i++)
				AddPhotoToGrid(items[i], i, columns, itemWidth, grid);

			PhotoSectionsPanel.Children.Add(grid);
		}

		private void AddPhotoToGrid(PhotoItem photo, int index, int columns, double itemWidth, Grid targetGrid) {
			int col = index % columns;
			int row = index / columns;

			if (targetGrid.RowDefinitions.Count <= row) {
				targetGrid.RowDefinitions.Add(new RowDefinition {
					Height = new GridLength(itemWidth)
				});
			}

			var cell = new Grid {
				Width = itemWidth,
				Height = itemWidth,
				Margin = new Thickness(4)
			};
			BitmapImage bmp = null;
			try {
				bmp = new BitmapImage();
				bmp.DecodePixelWidth = (int)itemWidth;
				using (var iso = IsolatedStorageFile.GetUserStoreForApplication()) {
					if (iso.FileExists(photo.ThumbPath)) {
						using (var stream = iso.OpenFile(photo.ThumbPath, FileMode.Open, FileAccess.Read)) {
							bmp.SetSource(stream);
						}
					} else {
						// Thumb file does not exist, use error image
						Debug.WriteLine($"Thumb file not found: {photo.ThumbPath} for media: {photo.FullPath}");
						bmp = new BitmapImage(new Uri("/Assets/imagerror.png", UriKind.Relative));

						// Try to regenerate the thumbnail asynchronously for next time using PhotoCache
						Task.Run(async () => {
							try {
								if (!string.IsNullOrEmpty(photo.FullPath)) {
									var file = await StorageFile.GetFileFromPathAsync(photo.FullPath);
									await Lummich.Models.PhotoCache.GetOrCreateThumbnailAsync(file, photo.Type);
								}
							} catch (Exception regenEx) {
								Debug.WriteLine($"Failed to regenerate thumb for {photo.FullPath}: {regenEx.Message}");
							}
						});
					}
				}
			} catch (Exception ex) {
				Debug.WriteLine($"Error loading thumb for media: {photo.FullPath}\nThumb path: {photo.ThumbPath}\nException: {ex.ToString()}\n\n");
				try {
					//try load Assets/imagerror.png as replacement
					bmp = new BitmapImage(new Uri("/Assets/imagerror.png", UriKind.Relative));
				} catch (Exception ex2) {
					Debug.WriteLine($"Also failed to load error image: {ex2.ToString()}");
				}
			}

			cell.Tap += (s, e) =>
			{
				NavigationService.Navigate(
					new Uri("/Layouts/PhotoViewPage.xaml?id=" + photo.Id, UriKind.Relative)
				);
			};
			cell.Tag = photo;

			_cells.Add(cell);


			var img = new Image {
				Width = itemWidth,
				Height = itemWidth,
				Stretch = System.Windows.Media.Stretch.UniformToFill,
				Source = bmp
			};

			var status = new Image {
				//Source = new BitmapImage(new Uri(photo.IsUploaded ? "/Assets/;" : "/Assets/no.png", UriKind.Relative)),
                Source = photo.IsUploaded ? uploadedIndicator : notUploadedIndicator,
				Width = 24,
				Height = 24,
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Bottom,
				Margin = new Thickness(0, 0, 11, 6),
				Tag = "status"
			};
			cell.Children.Add(img);
			cell.Children.Add(status);

			// VIDEO LENGTH LABEL
			if (photo.Type == MediaType.Video) {
				System.Diagnostics.Debug.WriteLine("File is video: " + photo.FullPath);
				System.Diagnostics.Debug.WriteLine("File is video: " + photo.FullPath);
				System.Diagnostics.Debug.WriteLine("File is video: " + photo.FullPath);
				TimeSpan t = TimeSpan.FromSeconds(photo.LengthSeconds);

				string duration = t.Hours > 0 ? $"{t.Hours:D2}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes:D2}:{t.Seconds:D2}";

				var durationLabel = new TextBlock {
					Text = duration,
					Foreground = new SolidColorBrush(Colors.White),
					FontSize = 16,
					FontWeight = FontWeights.Bold,
					HorizontalAlignment = HorizontalAlignment.Right,
					VerticalAlignment = VerticalAlignment.Top,
					Margin = new Thickness(0, 2, 16, 0),
				};

				cell.Children.Add(durationLabel);
			}




			Grid.SetColumn(cell, col);
			Grid.SetRow(cell, row);

			targetGrid.Children.Add(cell);
		}

        BitmapImage uploadedIndicator;
        BitmapImage notUploadedIndicator;
        /// <summary>
        /// Najde Grid s daným PhotoItem a aktualizuje IsUploaded indikátor (status image).
        /// </summary>
        public void UpdatePhotoIsUploadedIndicator(PhotoItem updatedPhoto) {
			if (updatedPhoto == null) return;
			foreach (var cell in _cells) {
				if (cell == null || cell.Tag == null) continue;
				var photo = cell.Tag as PhotoItem;
				if (photo == null) continue;
				if (photo.Id == updatedPhoto.Id) {
					// Najdi status image (indikátor) podle Tagu
					foreach (var child in cell.Children) {
						var statusImg = child as Image;
						if (statusImg != null && statusImg.Tag != null && statusImg.Tag.ToString() == "status") {
                            //statusImg.Source = new BitmapImage(new Uri(updatedPhoto.IsUploaded ? "/Assets/yes.png" : "/Assets/no.png", UriKind.Relative));
                            statusImg.Source = updatedPhoto.IsUploaded ? uploadedIndicator : notUploadedIndicator;

                            break;
						}
					}
					break;
				}
			}
		}


        private async void PhotosPerRowBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			int value;
			if (PhotosPerRowBox == null) return;
			if (int.TryParse(PhotosPerRowBox.Text, out value)) {
				if (value < 2) value = 2;
				if (value > 6) value = 6;
				photosPerRow = value;
				if (PhotosPerRowBox.Text != value.ToString())
				{
					PhotosPerRowBox.Text = value.ToString();
					PhotosPerRowBox.SelectionStart = PhotosPerRowBox.Text.Length;
				}
				// Save to settings
				IsolatedStorageSettings.ApplicationSettings["PhotosPerRow"] = value;
				IsolatedStorageSettings.ApplicationSettings.Save();
			}
			await PopulateGridWithProgress(AppState.Photos);
		}
		private ScrollViewer FindScrollViewer(DependencyObject parent) {
			if (parent is ScrollViewer)
				return (ScrollViewer)parent;

			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) {
				var child = VisualTreeHelper.GetChild(parent, i);
				var result = FindScrollViewer(child);
				if (result != null)
					return result;
			}

			return null;
		}

		// --- SERVER SETTINGS GUI ---
		private List<TextBox> ssidTextBoxes = new List<TextBox>();

		private void AddSSIDTextBox(string value = "") {
			var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0), Orientation = System.Windows.Controls.Orientation.Horizontal };
			var tb = new TextBox { Width = 300, Text = value };
			var btn = new Button { Content = "X", Width = 80, Margin = new Thickness(8, 0, 0, 0) };
			btn.Click += (s, e) => {
				SSIDListPanel.Children.Remove(sp);
				ssidTextBoxes.Remove(tb);
                // Autosave on remove

                string password = PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : PasswordTextBox.Text;
                ImmichApi.SaveAllSettings(EmailBox, password, GlobalDomainBox, LocalIpBox, ssidTextBoxes);
			};
			tb.TextChanged += ServerSettings_AutoSave;
			sp.Children.Add(tb);
			sp.Children.Add(btn);
			SSIDListPanel.Children.Add(sp);
			ssidTextBoxes.Add(tb);
		}

		private void AddSSIDButton_Click(object sender, RoutedEventArgs e) {
			AddSSIDTextBox("");
            // Autosave on add
            string password = PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : PasswordTextBox.Text;
            ImmichApi.SaveAllSettings(EmailBox, password, GlobalDomainBox, LocalIpBox, ssidTextBoxes);
		}

		// Autosave handler for server settings
		private void ServerSettings_AutoSave(object sender, EventArgs e) {
            string password = PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : PasswordTextBox.Text;
			ImmichApi.SaveAllSettings(EmailBox, password, GlobalDomainBox, LocalIpBox, ssidTextBoxes);
		}

		private void SaveServerSettingsButton_Click(object sender, RoutedEventArgs e) {
            string password = PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : PasswordTextBox.Text;
            ImmichApi.SaveAllSettings(EmailBox, password, GlobalDomainBox, LocalIpBox, ssidTextBoxes);
			MessageBox.Show("Nastavení serveru uloženo.");
		}

		private void ServerSettingsPanel_Loaded(object sender, RoutedEventArgs e) {
			   ImmichApi.LoadAllSettings(EmailBox, PasswordBox, GlobalDomainBox, LocalIpBox, SSIDListPanel, ssidTextBoxes, AddSSIDTextBox);
			   // Set IgnoreSslErrorsCheckBox state from HttpHelper
			   if (IgnoreSslErrorsCheckBox != null)
				   IgnoreSslErrorsCheckBox.IsChecked = HttpHelper.IgnoreSSLErrors;
			   if (IgnoreSslErrorsCheckBox != null)
			   {
				   IgnoreSslErrorsCheckBox.Checked -= IgnoreSslErrorsCheckBox_Checked;
				   IgnoreSslErrorsCheckBox.Unchecked -= IgnoreSslErrorsCheckBox_Unchecked;
				   IgnoreSslErrorsCheckBox.Checked += IgnoreSslErrorsCheckBox_Checked;
				   IgnoreSslErrorsCheckBox.Unchecked += IgnoreSslErrorsCheckBox_Unchecked;
			   }
		   }

		   private void IgnoreSslErrorsCheckBox_Checked(object sender, RoutedEventArgs e)
		   {
			   HttpHelper.IgnoreSSLErrors = true;
            string password = PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : PasswordTextBox.Text;
            ImmichApi.SaveAllSettings(EmailBox, password, GlobalDomainBox, LocalIpBox, ssidTextBoxes);
		   }

		   private void IgnoreSslErrorsCheckBox_Unchecked(object sender, RoutedEventArgs e)
		   {
			   HttpHelper.IgnoreSSLErrors = false;
            string password = PasswordBox.Visibility == Visibility.Visible ? PasswordBox.Password : PasswordTextBox.Text;
            ImmichApi.SaveAllSettings(EmailBox, password, GlobalDomainBox, LocalIpBox, ssidTextBoxes);
		}
		private async void NetTest_Click(object sender, RoutedEventArgs e) {
			if (NetworkTestOverlay != null){
				NetworkTestOverlay.Visibility = Visibility.Visible;
                CloseTestOverlayButton.IsEnabled = false;
            }
			if (NetworkTestResultText != null){
				NetworkTestResultText.Text = "";
			}
			await ImmichApi.NetworkTestAsync(NetworkTestResultText);
            Debug.WriteLine("reenabled network test overlay button");
            CloseTestOverlayButton.IsEnabled = true;

        }
        private void CloseNetworkTestOverlay_Click(object sender, RoutedEventArgs e) {
            NetworkTestOverlay.Visibility = Visibility.Collapsed;
        }


        // Load data for the ViewModel Items
        protected override void OnNavigatedTo(NavigationEventArgs e) {
			if (!App.ViewModel.IsDataLoaded) {
				App.ViewModel.LoadData();
			}
		}

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) {
            Debug.WriteLine("Refresh button click");
            await RescanPhotos();
        }

        private async void ButtonRefreshStats_Click(object sender, RoutedEventArgs e) {
            await ImmichServerStats.RefreshStats();

            int localVideos = 0;
            int localPhotos = 0;
            ulong FileSizeTotal = 0;
            PhotoItem.GetNumberOfMedia(AppState.Photos, out localPhotos, out localVideos, out FileSizeTotal);

            LocalPhotosCount.Text = localPhotos.ToString();
            LocalVideosCount.Text = localVideos.ToString();

            LocalDataSize.Text = PhotoViewPage.FormatSize((long)FileSizeTotal);

            UploadedCount.Text = ImmichServerStats.MediaSentToServerCount.ToString();
            ServerDataSize.Text = $"{PhotoViewPage.FormatSize((long)ImmichServerStats.DiskUsage)} / {PhotoViewPage.FormatSize((long)ImmichServerStats.DiskCapacity)}";



        }

        private void StartButton_Click(object sender, RoutedEventArgs e) {
			// Spustit hromadný upload s detailním callbackem
			if (AppState.Photos == null || AppState.Photos.Count == 0) {
				MessageBox.Show("Nejsou načteny žádné fotky.");
				return;
			}

			// Nastavit endpoint do UI
			CurrentEndpointText.Text = ImmichApi.GetServerIP();

			// Reset UI
			CurrentFileText.Text = "-";
			CurrentFileProgressText.Text = "-";
			CurrentFileProgress.Value = 0; CurrentFileProgress.Maximum = 100;
			FilesRemainingText.Text = "-";
			TotalProgressText.Text = "-";
			TotalProgress.Value = 0; TotalProgress.Maximum = 100;

			// Spustit upload na pozadí
			Task.Run(async () => {
				await ImmichApi.BulkUploadAsync(AppState.Photos, (sessionUploadCount, alreadyUploadedCount, currentFile, currentFileUploaded, currentFileSize, sessionUploadedBytes, sessionTotalBytes) => {
					Dispatcher.BeginInvoke(() => {
						// 1) Kolik fotek se uploaduje v této session
						// 2) Kolik už bylo nahráno
						// 3) Aktuální soubor
						// 4) Kolik bytů aktuálního souboru nahráno
						// 5) Velikost aktuálního souboru
						// 6) Kolik bytů celkem nahráno v session
						// 7) Kolik bytů mají všechny soubory celkem

						// Aktuální soubor
						CurrentFileText.Text = currentFile ?? "-";

						// Průběh aktuálního souboru
						double percentFile = currentFileSize > 0 ? (double)currentFileUploaded / currentFileSize * 100.0 : 0;
						CurrentFileProgress.Maximum = 100;
						CurrentFileProgress.Value = percentFile;
						string uploadedStr = PhotoViewPage.FormatSize((long)currentFileUploaded);
						string totalStr = PhotoViewPage.FormatSize((long)currentFileSize);
						CurrentFileProgressText.Text = $"{percentFile:F1}% {uploadedStr} / {totalStr}";

						// Celkový průběh
						int filesDone = alreadyUploadedCount + (sessionUploadedBytes > 0 && currentFileUploaded == currentFileSize ? 1 : 0);
						FilesRemainingText.Text = $"{filesDone} / {sessionUploadCount + alreadyUploadedCount}";

						double percentTotal = sessionTotalBytes > 0 ? (double)sessionUploadedBytes / sessionTotalBytes * 100.0 : 0;
						TotalProgress.Maximum = 100;
						TotalProgress.Value = percentTotal;
						string uploadedTotalStr = PhotoViewPage.FormatSize((long)sessionUploadedBytes);
						string totalTotalStr = PhotoViewPage.FormatSize((long)sessionTotalBytes);
						TotalProgressText.Text = $"{percentTotal:F1}% {uploadedTotalStr} / {totalTotalStr}";
					});
				});
			});
		}
    }
}