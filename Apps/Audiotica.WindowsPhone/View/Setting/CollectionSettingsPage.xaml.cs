﻿#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Audiotica.Core.Utilities;
using Audiotica.Core.Utils;
using Audiotica.Core.WinRt.Common;

using PCLStorage;

using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;

using Xamarin;

#endregion

namespace Audiotica.View.Setting
{
    public sealed partial class CollectionSettingsPage : IFileSavePickerContinuable, IFileOpenPickerContinuable
    {
        public CollectionSettingsPage()
        {
            InitializeComponent();
        }

        private void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.Locator.Download.ActiveDownloads.Count > 0)
            {
                CurtainPrompt.ShowError("Can't do a backup while there are active downloads.");
                return;
            }

            var savePicker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };

            // Dropdown of file types the user can save the file as
            savePicker.FileTypeChoices.Add("Audiotica Backup", new List<string> { ".autcp" });

            // Default file name if the user does not type one in or select a file to replace
            savePicker.SuggestedFileName = string.Format("{0}-WP81", DateTime.Now.ToString("MM-dd-yy_H.mm"));

            savePicker.PickSaveFileAndContinue();
        }

        private async void Import()
        {
            using (var handle = Insights.TrackTime("Import Music"))
            {
                UiBlockerUtility.Block("Scanning...");
                var localMusic = await LocalMusicHelper.GetFilesInMusicAsync();
                handle.Data.Add("TotalCount", localMusic.Count.ToString());
                var failedCount = 0;

                App.Locator.CollectionService.Songs.SuppressEvents = true;
                App.Locator.CollectionService.Artists.SuppressEvents = true;
                App.Locator.CollectionService.Albums.SuppressEvents = true;

                App.Locator.SqlService.BeginTransaction();
                for (var i = 0; i < localMusic.Count; i++)
                {
                    StatusBarHelper.ShowStatus(
                        string.Format("Importing {0} of {1} items", i + 1, localMusic.Count), 
                        (double)i / localMusic.Count);
                    try
                    {
                        await LocalMusicHelper.SaveTrackAsync(localMusic[i]);
                    }
                    catch
                    {
                        failedCount++;
                    }
                }

                App.Locator.SqlService.Commit();

                App.Locator.CollectionService.Songs.Reset();
                App.Locator.CollectionService.Artists.Reset();
                App.Locator.CollectionService.Albums.Reset();

                UiBlockerUtility.Unblock();

                if (failedCount > 0)
                {
                    CurtainPrompt.ShowError("Couldn't import {0} song(s).", failedCount);
                    handle.Data.Add("Failed", failedCount.ToString());
                }
            }

            await CollectionHelper.DownloadAlbumsArtworkAsync();
            await CollectionHelper.DownloadArtistsArtworkAsync();
        }

        private void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (!App.Locator.CollectionService.IsLibraryLoaded)
            {
                UiBlockerUtility.Block("Loading collection...");
                App.Locator.CollectionService.LibraryLoaded += (o, args) =>
                {
                    UiBlockerUtility.Unblock();
                    Import();
                };
            }
            else
            {
                Import();
            }
        }

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.Locator.Download.ActiveDownloads.Count > 0)
            {
                CurtainPrompt.ShowError("Can't do a reset while there are active downloads.");
                return;
            }

            if (
                await
                MessageBox.ShowAsync(
                    "This will delete completely all your downloaded and saved songs, along with their artwork. Imported songs will only be removed from the app.", 
                    "ARE YOU SURE?", 
                    MessageBoxButton.YesNo) == MessageBoxResult.No)
            {
                return;
            }

            await
                MessageBox.ShowAsync(
                    "To continue with the factory reset the app will shutdown and continue once you open it again.", 
                    "Application Restart Required");


            App.Locator.AppSettingsHelper.Write("FactoryReset", true);
            App.Locator.AppSettingsHelper.Write("LastSyncTime", null);
            App.Locator.AudioPlayerHelper.FullShutdown();
            App.Locator.SqlService.Dispose();
            App.Locator.BgSqlService.Dispose();
            Application.Current.Exit();
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (App.Locator.Download.ActiveDownloads.Count > 0)
            {
                CurtainPrompt.ShowError("Can't do a restore while there are active downloads.");
                return;
            }

            if (
                await
                MessageBox.ShowAsync(
                    "This will delete all your pre-existing data.", 
                    "Continue with Restore?", 
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;
            }

            var fileOpenPicker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            fileOpenPicker.FileTypeFilter.Add(".autcp");
            fileOpenPicker.PickSingleFileAndContinue();
        }

        #region implementations

        public async void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args)
        {
            var file = args.Files.FirstOrDefault();

            if (file == null)
            {
                CurtainPrompt.ShowError("No backup file picked.");
                return;
            }

            UiBlockerUtility.Block("Preparing...");
            App.Locator.AudioPlayerHelper.FullShutdown();
            App.Locator.SqlService.Dispose();
            App.Locator.BgSqlService.Dispose();

            using (var stream = await file.OpenStreamForReadAsync())
            {
                if (AutcpFormatHelper.ValidateHeader(stream))
                {
                    stream.Seek(0, SeekOrigin.Begin);
                    var restoreFile = await StorageHelper.CreateFileAsync("_current_restore.autcp");

                    using (var restoreStream = await restoreFile.OpenAsync(FileAccess.ReadAndWrite))
                    {
                        await stream.CopyToAsync(restoreStream);
                    }

                    UiBlockerUtility.Unblock();

                    await
                        MessageBox.ShowAsync(
                            "To finish applying the restore the app will close. Next time you start the app, it will finish restoring.", 
                            "Application Restart Required");

                    App.Locator.AppSettingsHelper.Write("Restore", true);
                    Application.Current.Exit();
                }
                else
                {
                    CurtainPrompt.ShowError("Not a valid backup file.");
                }
            }
        }

        public async void ContinueFileSavePicker(FileSavePickerContinuationEventArgs args)
        {
            var file = args.File;

            if (file == null)
            {
                CurtainPrompt.ShowError("Backup cancelled.");
                return;
            }

            using (Insights.TrackTime("Create Backup"))
            {
                UiBlockerUtility.Block("Backing up (this may take a bit)...");

                App.Locator.SqlService.Dispose();
                App.Locator.BgSqlService.Dispose();

                try
                {
                    var data = await AutcpFormatHelper.CreateBackup(ApplicationData.Current.LocalFolder);
                    using (var stream = await file.OpenStreamForWriteAsync())
                    {
                        await stream.WriteAsync(data, 0, data.Length);
                    }
                }
                catch (Exception e)
                {
                    Insights.Report(e, "Where", "Creating Backup");
                    CurtainPrompt.ShowError("Problem creating backup.");
                }

                App.Locator.SqlService.Initialize();
                App.Locator.BgSqlService.Initialize();
                UiBlockerUtility.Unblock();
            }

            CurtainPrompt.Show("Backup completed.");
        }

        #endregion
    }
}