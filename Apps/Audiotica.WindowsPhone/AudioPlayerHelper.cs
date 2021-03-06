﻿#region

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation.Collections;
using Windows.Media.Playback;
using Audiotica.Core;
using Audiotica.Core.Utils.Interfaces;
using Audiotica.Core.WinRt.Common;
using Audiotica.Core.WinRt.Utilities;
using Audiotica.Data.Collection.Model;
using GalaSoft.MvvmLight.Threading;
using Xamarin;

#endregion

namespace Audiotica
{
    public class AudioPlayerHelper
    {
        private readonly IAppSettingsHelper _appSettings;
        private bool _isShutdown = true;
        private int _retryCount;

        public AudioPlayerHelper(IAppSettingsHelper appSettings)
        {
            _appSettings = appSettings;
        }

        public MediaPlayer SafeMediaPlayer
        {
            get
            {
                try
                {
                    return BackgroundMediaPlayer.Current;
                }
                catch
                {
                    if (_retryCount >= 5)
                    {
                        _retryCount = 0;
                        return null;
                    }
                    _retryCount++;
                    return SafeMediaPlayer;
                }
            }
        }

        public MediaPlayerState SafePlayerState
        {
            get
            {
                var player = SafeMediaPlayer;

                try
                {
                    return player == null ? MediaPlayerState.Closed : player.CurrentState;
                }
                catch
                {
                    if (_retryCount >= 5)
                    {
                        _retryCount = 0;
                        return MediaPlayerState.Closed;
                    }
                    _retryCount++;
                    return SafePlayerState;
                }
            }
        }

        public event EventHandler Shutdown;
        public event EventHandler TrackChanged;
        public event EventHandler<PlaybackStateEventArgs> PlaybackStateChanged;

        public void FullShutdown()
        {
            var player = SafeMediaPlayer;

            if (player == null)
            {
                return;
            }

            RemoveMediaPlayerEventHandlers(player);
            BackgroundMediaPlayer.Shutdown();
            App.Locator.AppSettingsHelper.Write(PlayerConstants.CurrentTrack, null);
        }

        public void StartStation(int id)
        {
            _appSettings.Write("RadioId", id);
            _appSettings.Write("RadioMode", true);
            var value = new ValueSet { { PlayerConstants.StartStation, string.Empty } };
            BackgroundMediaPlayer.SendMessageToBackground(value);
        }

        public void NextSong()
        {
            var value = new ValueSet {{PlayerConstants.SkipNext, string.Empty}};
            BackgroundMediaPlayer.SendMessageToBackground(value);
        }

        public void OnAppActive()
        {
            App.Locator.AppSettingsHelper.Write(PlayerConstants.AppState, PlayerConstants.ForegroundAppActive);

            AddMediaPlayerEventHandlers();
            RaiseEvent(TrackChanged);
            OnPlaybackStateChanged(SafePlayerState);
        }

        public void OnAppSuspending()
        {
            App.Locator.AppSettingsHelper.Write(PlayerConstants.AppState, PlayerConstants.ForegroundAppSuspended);
            RemoveMediaPlayerEventHandlers(SafeMediaPlayer);
        }

        public void OnShuffleChanged()
        {
            RaiseEvent(TrackChanged);
        }

        public void PlayPauseToggle()
        {
            var player = SafeMediaPlayer;

            if (player == null)
            {
                return;
            }

            switch (SafePlayerState)
            {
                case MediaPlayerState.Playing:
                    player.Pause();
                    break;
                case MediaPlayerState.Paused:
                    player.Play();
                    break;
            }
        }

        public async void PlaySong(QueueSong song)
        {
            if (song == null || song.Song == null)
            {
                CurtainPrompt.ShowError("Song seems to be empty...");
                return;
            }

            Insights.Track(
                "Play Song",
                new Dictionary<string, string>
                {
                    {"Name", song.Song.Name},
                    {"ArtistName", song.Song.ArtistName},
                    {"ProviderId", song.Song.ProviderId}
                });

            if (_isShutdown)
            {
                await AddMediaPlayerEventHandlers();
            }

            _appSettings.Write("RadioId", null);
            _appSettings.Write("RadioMode", false);
            _appSettings.Write(PlayerConstants.CurrentTrack, song.Id);

            var message = new ValueSet {{PlayerConstants.StartPlayback, null}};
            BackgroundMediaPlayer.SendMessageToBackground(message);

            RaiseEvent(TrackChanged);
        }

        public void PrevSong()
        {
            var value = new ValueSet {{PlayerConstants.SkipPrevious, string.Empty}};
            BackgroundMediaPlayer.SendMessageToBackground(value);
        }

        public async Task ShutdownPlayerAsync()
        {
            var player = SafeMediaPlayer;

            if (player == null)
            {
                return;
            }

            RemoveMediaPlayerEventHandlers(player);
            BackgroundMediaPlayer.Shutdown();
            App.Locator.AppSettingsHelper.Write(PlayerConstants.CurrentTrack, null);
            await Task.Delay(500);
            _isShutdown = true;
            RaiseEvent(Shutdown);
        }

        protected virtual void OnPlaybackStateChanged(MediaPlayerState state)
        {
            DispatcherHelper.RunAsync(
                () =>
                {
                    var handler = PlaybackStateChanged;
                    if (handler != null)
                    {
                        handler(this, new PlaybackStateEventArgs(state));
                    }
                });
        }

        private async Task AddMediaPlayerEventHandlers()
        {
            var player = SafeMediaPlayer;

            if (player == null)
            {
                return;
            }

            try
            {
                // avoid duplicate events
                RemoveMediaPlayerEventHandlers(player);
                player.CurrentStateChanged += MediaPlayer_CurrentStateChanged;
                BackgroundMediaPlayer.MessageReceivedFromBackground +=
                    BackgroundMediaPlayer_MessageReceivedFromBackground;
                _isShutdown = false;
                await Task.Delay(250);
                return;
            }
            catch
            {
                // ignored
            }

            if (_retryCount >= 5)
            {
                _isShutdown = true;
                _retryCount = 0;
                return;
            }

            _retryCount++;
            await AddMediaPlayerEventHandlers();
        }

        private void BackgroundMediaPlayer_MessageReceivedFromBackground(
            object sender,
            MediaPlayerDataReceivedEventArgs e)
        {
            foreach (var key in e.Data.Keys)
            {
                switch (key)
                {
                    case PlayerConstants.Trackchanged:
                        RaiseEvent(TrackChanged);
                        break;
                }
            }
        }

        private void MediaPlayer_CurrentStateChanged(MediaPlayer sender, object args)
        {
            OnPlaybackStateChanged(SafePlayerState);
        }

        private void RaiseEvent(EventHandler handler)
        {
            DispatcherHelper.RunAsync(
                () =>
                {
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }
                });
        }

        private void RemoveMediaPlayerEventHandlers(MediaPlayer player)
        {
            player.CurrentStateChanged -= MediaPlayer_CurrentStateChanged;
            BackgroundMediaPlayer.MessageReceivedFromBackground -= BackgroundMediaPlayer_MessageReceivedFromBackground;
        }
    }

    public class PlaybackStateEventArgs : EventArgs
    {
        public PlaybackStateEventArgs(MediaPlayerState state)
        {
            State = state;
        }

        public MediaPlayerState State { get; set; }
    }
}