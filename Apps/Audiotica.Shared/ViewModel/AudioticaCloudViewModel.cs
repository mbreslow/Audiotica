﻿using Audiotica.Core.Utils.Interfaces;
using Audiotica.Core.WinRt.Common;
using Audiotica.Data.Service.Interfaces;
using Audiotica.PartialView;
using Audiotica.View.Setting;

using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;

namespace Audiotica.ViewModel
{
    public class AudioticaCloudViewModel : ViewModelBase
    {
        private readonly IAppSettingsHelper _appSettingsHelper;

        public AudioticaCloudViewModel(IAudioticaService service, IAppSettingsHelper appSettingsHelper)
        {
            _appSettingsHelper = appSettingsHelper;
            this.Service = service;
            this.SignInCommand = new RelayCommand(this.SignInExecute);
            this.SignUpCommand = new RelayCommand(this.SignUpExecute);
            this.SubscribeCommand = new RelayCommand(this.SubscribeExecute);
            this.LogoutCommand = new RelayCommand(this.LogoutExecute);
        }

        public RelayCommand LogoutCommand { get; set; }

        public IAudioticaService Service { get; private set; }

        public RelayCommand SignInCommand { get; set; }

        public RelayCommand SignUpCommand { get; set; }

        public RelayCommand SubscribeCommand { get; set; }

        private void LogoutExecute()
        {
            this.Service.Logout();
            _appSettingsHelper.Write("LastSyncTime", null);
            CurtainPrompt.Show("Goodbye!");
        }

        private async void SignInExecute()
        {
            var signInSheet = new EmailSignInSheet();
            var success = await ModalSheetUtility.ShowAsync(signInSheet);

            if (success)
            {
                CurtainPrompt.Show("Welcome!");
                CollectionHelper.IdentifyXamarin();

                // Sync collection
                await CollectionHelper.CloudSync(false);
                await CollectionHelper.DownloadAlbumsArtworkAsync();
                await CollectionHelper.DownloadArtistsArtworkAsync();
            }
        }

        private async void SignUpExecute()
        {
            var signInSheet = new EmailSignUpSheet();
            var success = await ModalSheetUtility.ShowAsync(signInSheet);

            if (!success) return;

            CurtainPrompt.Show("Welcome!");
            CollectionHelper.IdentifyXamarin();

            SubscribeExecute();
        }

        private void SubscribeExecute()
        {
            App.Navigator.GoTo<CloudSubscribePage, ZoomInTransition>(null);
        }
    }
}