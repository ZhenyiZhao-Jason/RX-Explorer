﻿using Microsoft.Toolkit.Deferred;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Devices.Enumeration;
using Windows.Devices.Portable;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Imaging;

namespace RX_Explorer.Class
{
    public static class CommonAccessCollection
    {
        public static ObservableCollection<DriveDataBase> DriveList { get; } = new ObservableCollection<DriveDataBase>();
        public static ObservableCollection<LibraryStorageFolder> LibraryFolderList { get; } = new ObservableCollection<LibraryStorageFolder>();
        public static ObservableCollection<QuickStartItem> QuickStartList { get; } = new ObservableCollection<QuickStartItem>();
        public static ObservableCollection<QuickStartItem> WebLinkList { get; } = new ObservableCollection<QuickStartItem>();


        private static readonly DeviceWatcher PortalDriveWatcher = DeviceInformation.CreateWatcher(DeviceClass.PortableStorageDevice);

        private static readonly DispatcherTimer NetworkDriveCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        public static event EventHandler<DriveChangedDeferredEventArgs> DriveAdded;

        public static event EventHandler<DriveChangedDeferredEventArgs> DriveRemoved;

        public static event EventHandler<Queue<string>> LibraryNotFound;

        public static event EventHandler<LibraryChangedDeferredEventArgs> LibraryAdded;

        public static event EventHandler<LibraryChangedDeferredEventArgs> LibraryRemoved;

        public static bool IsLibaryLoaded { get; private set; }

        public static bool IsDriveLoaded { get; private set; }

        public static bool IsQuickStartLoaded { get; private set; }

        private static int LoadDriveLockResource;
        private static int LoadLibraryLockResource;
        private static int LoadQuickStartLockResource;

        public static async Task LoadQuickStartItemsAsync(bool IsRefresh = false)
        {
            if (Interlocked.Exchange(ref LoadQuickStartLockResource, 1) == 0)
            {
                try
                {
                    if (!IsQuickStartLoaded || IsRefresh)
                    {
                        IsQuickStartLoaded = true;

                        if (IsRefresh)
                        {
                            QuickStartList.Clear();
                            WebLinkList.Clear();
                        }

                        foreach ((string Name, string IconPath, string Protocal, string Type) in SQLite.Current.GetQuickStartItem())
                        {
                            StorageFile ImageFile = null;

                            try
                            {
                                ImageFile = IconPath.StartsWith("ms-appx") ? await StorageFile.GetFileFromApplicationUriAsync(new Uri(IconPath))
                                                                       : await StorageFile.GetFileFromPathAsync(Path.Combine(ApplicationData.Current.LocalFolder.Path, IconPath));

                                BitmapImage Bitmap = new BitmapImage();

                                using (IRandomAccessStream Stream = await ImageFile.OpenAsync(FileAccessMode.Read))
                                {
                                    await Bitmap.SetSourceAsync(Stream);
                                }

                                if (Enum.Parse<QuickStartType>(Type) == QuickStartType.Application)
                                {
                                    QuickStartList.Add(new QuickStartItem(QuickStartType.Application, Bitmap, Protocal, IconPath, Name));
                                }
                                else
                                {
                                    WebLinkList.Add(new QuickStartItem(QuickStartType.WebSite, Bitmap, Protocal, IconPath, Name));
                                }
                            }
                            catch (Exception ex)
                            {
                                LogTracer.Log(ex, $"Could not load QuickStart item, Name: {Name}");

                                SQLite.Current.DeleteQuickStartItem(Name, Protocal, IconPath, Type);

                                if (ImageFile != null)
                                {
                                    await ImageFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                                }
                            }
                        }

                        QuickStartList.Add(new QuickStartItem());
                        WebLinkList.Add(new QuickStartItem());
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex);
                }
                finally
                {
                    Interlocked.Exchange(ref LoadQuickStartLockResource, 0);
                }
            }
        }

        public static async Task LoadLibraryFoldersAsync(bool IsRefresh = false)
        {
            if (Interlocked.Exchange(ref LoadLibraryLockResource, 1) == 0)
            {
                try
                {
                    if (!IsLibaryLoaded || IsRefresh)
                    {
                        IsLibaryLoaded = true;

                        if (IsRefresh)
                        {
                            LibraryFolderList.Clear();
                        }

                        try
                        {
                            IReadOnlyList<User> UserList = await User.FindAllAsync();

                            UserDataPaths DataPath = UserList.FirstOrDefault((User) => User.AuthenticationStatus == UserAuthenticationStatus.LocallyAuthenticated && User.Type == UserType.LocalUser) is User CurrentUser
                                                     ? UserDataPaths.GetForUser(CurrentUser)
                                                     : UserDataPaths.GetDefault();
                            try
                            {
                                List<(LibraryType, string)> Array = new List<(LibraryType, string)>();

                                if (!string.IsNullOrEmpty(DataPath.Downloads))
                                {
                                    Array.Add((LibraryType.Downloads, DataPath.Downloads));
                                }

                                if (!string.IsNullOrEmpty(DataPath.Desktop))
                                {
                                    Array.Add((LibraryType.Desktop, DataPath.Desktop));
                                }

                                if (!string.IsNullOrEmpty(DataPath.Videos))
                                {
                                    Array.Add((LibraryType.Videos, DataPath.Videos));
                                }

                                if (!string.IsNullOrEmpty(DataPath.Pictures))
                                {
                                    Array.Add((LibraryType.Pictures, DataPath.Pictures));
                                }

                                if (!string.IsNullOrEmpty(DataPath.Documents))
                                {
                                    Array.Add((LibraryType.Document, DataPath.Documents));
                                }

                                if (!string.IsNullOrEmpty(DataPath.Music))
                                {
                                    Array.Add((LibraryType.Music, DataPath.Music));
                                }

                                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OneDrive")))
                                {
                                    Array.Add((LibraryType.OneDrive, Environment.GetEnvironmentVariable("OneDrive")));
                                }

                                SQLite.Current.UpdateLibraryPath(Array);
                            }
                            catch (Exception ex)
                            {
                                LogTracer.Log(ex, "An error was threw when getting library folder (In initialize)");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogTracer.Log(ex, "An error was threw when try to get 'UserDataPath' (In initialize)");

                            List<(LibraryType, string)> Array = new List<(LibraryType, string)>();

                            string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                            if (!string.IsNullOrEmpty(DesktopPath))
                            {
                                Array.Add((LibraryType.Desktop, DesktopPath));
                            }

                            string VideoPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                            if (!string.IsNullOrEmpty(VideoPath))
                            {
                                Array.Add((LibraryType.Videos, VideoPath));
                            }

                            string PicturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                            if (!string.IsNullOrEmpty(PicturesPath))
                            {
                                Array.Add((LibraryType.Pictures, PicturesPath));
                            }

                            string DocumentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            if (!string.IsNullOrEmpty(DocumentsPath))
                            {
                                Array.Add((LibraryType.Document, DocumentsPath));
                            }

                            string MusicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                            if (!string.IsNullOrEmpty(MusicPath))
                            {
                                Array.Add((LibraryType.Music, MusicPath));
                            }

                            string OneDrivePath = Environment.GetEnvironmentVariable("OneDrive");
                            if (!string.IsNullOrEmpty(OneDrivePath))
                            {
                                Array.Add((LibraryType.OneDrive, OneDrivePath));
                            }

                            SQLite.Current.UpdateLibraryPath(Array);
                        }

                        Queue<string> ErrorList = new Queue<string>();

                        foreach ((string, LibraryType) Library in SQLite.Current.GetLibraryPath())
                        {
                            LibraryStorageFolder Lib = await LibraryStorageFolder.CreateAsync(Library.Item2, Library.Item1);

                            if (Lib != null)
                            {
                                LibraryFolderList.Add(Lib);
                            }
                            else
                            {
                                ErrorList.Enqueue(Library.Item1);
                                SQLite.Current.DeleteLibrary(Library.Item1);
                            }
                        }

                        await JumpListController.Current.AddItemAsync(JumpListGroup.Library, LibraryFolderList.Where((Library) => Library.LibType == LibraryType.UserCustom).Select((Library) => Library.Path).ToArray());

                        if (ErrorList.Count > 0)
                        {
                            LibraryNotFound?.Invoke(null, ErrorList);
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref LoadLibraryLockResource, 0);
                }
            }
        }

        public static async Task LoadDriveAsync(bool IsRefresh = false)
        {
            if (Interlocked.Exchange(ref LoadDriveLockResource, 1) == 0)
            {
                try
                {
                    if (!IsDriveLoaded || IsRefresh)
                    {
                        IsDriveLoaded = true;

                        if (IsRefresh)
                        {
                            DriveList.Clear();
                        }

                        foreach (DriveInfo Drive in DriveInfo.GetDrives().Where((Drives) => Drives.DriveType == DriveType.Fixed || Drives.DriveType == DriveType.Removable || Drives.DriveType == DriveType.Network)
                                                                         .Where((NewItem) => DriveList.All((Item) => !Item.Path.Equals(NewItem.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase))))
                        {
                            try
                            {
                                StorageFolder Folder = await StorageFolder.GetFolderFromPathAsync(Drive.RootDirectory.FullName);

                                if (DriveList.All((Item) => (string.IsNullOrEmpty(Item.Path) || string.IsNullOrEmpty(Folder.Path)) ? !Item.Name.Equals(Folder.Name, StringComparison.OrdinalIgnoreCase) : !Item.Path.Equals(Folder.Path, StringComparison.OrdinalIgnoreCase)))
                                {
                                    DriveList.Add(await DriveDataBase.CreateAsync(Drive.DriveType, Folder));
                                }
                            }
                            catch (Exception ex)
                            {
                                LogTracer.Log(ex, $"Hide the device \"{Drive.RootDirectory.FullName}\" for error");
                            }
                        }

                        foreach (DeviceInformation Drive in await DeviceInformation.FindAllAsync(StorageDevice.GetDeviceSelector()))
                        {
                            try
                            {
                                StorageFolder Folder = StorageDevice.FromId(Drive.Id);

                                if (DriveList.All((Item) => (string.IsNullOrEmpty(Item.Path) || string.IsNullOrEmpty(Folder.Path)) ? !Item.Name.Equals(Folder.Name, StringComparison.OrdinalIgnoreCase) : !Item.Path.Equals(Folder.Path, StringComparison.OrdinalIgnoreCase)))
                                {
                                    DriveList.Add(await DriveDataBase.CreateAsync(DriveType.Removable, Folder));
                                }
                            }
                            catch (Exception ex)
                            {
                                LogTracer.Log(ex, $"Hide the device for error");
                            }
                        }


                        foreach (StorageFolder WslFolder in await GetWslDriveAsync())
                        {
                            try
                            {
                                if (DriveList.All((Item) => (string.IsNullOrEmpty(Item.Path) || string.IsNullOrEmpty(WslFolder.Path)) ? !Item.Name.Equals(WslFolder.Name, StringComparison.OrdinalIgnoreCase) : !Item.Path.Equals(WslFolder.Path, StringComparison.OrdinalIgnoreCase)))
                                {
                                    DriveList.Add(await DriveDataBase.CreateAsync(DriveType.Network, WslFolder));
                                }
                            }
                            catch (Exception ex)
                            {
                                LogTracer.Log(ex, $"An exception was threw and hide the drive: \"{WslFolder.Name}\"");
                            }
                        }

                        switch (PortalDriveWatcher.Status)
                        {
                            case DeviceWatcherStatus.Created:
                            case DeviceWatcherStatus.Aborted:
                            case DeviceWatcherStatus.Stopped:
                                {
                                    PortalDriveWatcher.Start();
                                    break;
                                }
                        }

                        if (!NetworkDriveCheckTimer.IsEnabled)
                        {
                            NetworkDriveCheckTimer.Start();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, $"An exception was threw in {nameof(LoadDriveAsync)}");
                }
                finally
                {
                    Interlocked.Exchange(ref LoadDriveLockResource, 0);
                }
            }
        }

        private static async void PortalDriveWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            try
            {
                StorageFolder Folder = StorageDevice.FromId(args.Id);

                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    foreach (DriveDataBase Drive in DriveList.Where((Drive) => (string.IsNullOrEmpty(Drive.Path) || string.IsNullOrEmpty(Folder.Path)) ? Drive.Name.Equals(Folder.Name, StringComparison.OrdinalIgnoreCase) : Drive.Path.Equals(Folder.Path, StringComparison.OrdinalIgnoreCase)).ToArray())
                    {
                        DriveList.Remove(Drive);
                    }
                });
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An exception was threw when removing drive from {nameof(DriveList)}");
            }
        }

        private static async void PortalDriveWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            try
            {
                StorageFolder Folder = StorageDevice.FromId(args.Id);

                if (DriveList.All((Drive) => (string.IsNullOrEmpty(Drive.Path) || string.IsNullOrEmpty(Folder.Path)) ? !Drive.Name.Equals(Folder.Name, StringComparison.OrdinalIgnoreCase) : !Drive.Path.Equals(Folder.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                    {
                        DriveList.Add(await DriveDataBase.CreateAsync(DriveType.Removable, Folder));
                    });
                }
            }
            catch (Exception ex)
            {
                LogTracer.Log(ex, $"An exception was threw when adding drive to {nameof(DriveList)}");
            }
        }

        private async static void DriveList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    {
                        if (DriveAdded != null)
                        {
                            foreach (DriveDataBase Drive in e.NewItems)
                            {
                                await DriveAdded.InvokeAsync(null, new DriveChangedDeferredEventArgs(Drive));
                            }
                        }
                        break;
                    }
                case NotifyCollectionChangedAction.Remove:
                    {
                        if (DriveRemoved != null)
                        {
                            foreach (DriveDataBase Drive in e.OldItems)
                            {
                                await DriveRemoved.InvokeAsync(null, new DriveChangedDeferredEventArgs(Drive));
                            }
                        }

                        break;
                    }
            }
        }

        private async static Task<IReadOnlyList<StorageFolder>> GetWslDriveAsync()
        {
            try
            {
                StorageFolder WslBaseFolder = await StorageFolder.GetFolderFromPathAsync(@"\\wsl$");

                StorageFolderQueryResult Query = WslBaseFolder.CreateFolderQueryWithOptions(new QueryOptions
                {
                    FolderDepth = FolderDepth.Shallow,
                    IndexerOption = IndexerOption.DoNotUseIndexer
                });

                return await Query.GetFoldersAsync();
            }
            catch
            {
                return new List<StorageFolder>(0);
            }
        }

        private async static void NetworkDriveCheckTimer_Tick(object sender, object e)
        {
            NetworkDriveCheckTimer.Stop();

            DriveInfo[] NewNetworkDrive = DriveInfo.GetDrives().Where((Drives) => Drives.DriveType == DriveType.Network).ToArray();
            DriveDataBase[] ExistNetworkDrive = DriveList.OfType<NormalDriveData>().Where((ExistDrive) => ExistDrive.DriveType == DriveType.Network).ToArray();

            IEnumerable<DriveInfo> AddList = NewNetworkDrive.Where((NewDrive) => ExistNetworkDrive.All((ExistDrive) => !ExistDrive.Path.Equals(NewDrive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase)));
            IEnumerable<DriveDataBase> RemoveList = ExistNetworkDrive.Where((ExistDrive) => NewNetworkDrive.All((NewDrive) => !ExistDrive.Path.Equals(NewDrive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase)));

            foreach (DriveDataBase ExistDrive in RemoveList)
            {
                DriveList.Remove(ExistDrive);
            }

            foreach (DriveInfo Drive in AddList)
            {
                try
                {
                    DriveList.Add(await DriveDataBase.CreateAsync(Drive.DriveType, await StorageFolder.GetFolderFromPathAsync(Drive.RootDirectory.FullName)));
                }
                catch (Exception ex)
                {
                    LogTracer.Log(ex, $"Hide the device \"{Drive.RootDirectory.FullName}\" for error");
                }
            }

            NetworkDriveCheckTimer.Start();
        }

        static CommonAccessCollection()
        {
            PortalDriveWatcher.Added += PortalDriveWatcher_Added;
            PortalDriveWatcher.Removed += PortalDriveWatcher_Removed;
            DriveList.CollectionChanged += DriveList_CollectionChanged;
            LibraryFolderList.CollectionChanged += LibraryFolderList_CollectionChanged;
            NetworkDriveCheckTimer.Tick += NetworkDriveCheckTimer_Tick;
        }

        private static async void LibraryFolderList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    {
                        if (DriveAdded != null)
                        {
                            foreach (LibraryStorageFolder Lib in e.NewItems)
                            {
                                await LibraryAdded.InvokeAsync(null, new LibraryChangedDeferredEventArgs(Lib));
                            }
                        }

                        break;
                    }
                case NotifyCollectionChangedAction.Remove:
                    {
                        if (DriveRemoved != null)
                        {
                            foreach (LibraryStorageFolder Lib in e.OldItems)
                            {
                                await LibraryRemoved.InvokeAsync(null, new LibraryChangedDeferredEventArgs(Lib));
                            }
                        }

                        break;
                    }
            }
        }
    }
}
