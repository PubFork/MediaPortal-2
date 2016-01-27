#region Copyright (C) 2007-2015 Team MediaPortal

/*
    Copyright (C) 2007-2015 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Threading;
using MediaPortal.Common;
using MediaPortal.Common.General;
using MediaPortal.Common.Logging;
using MediaPortal.Common.MediaManagement;
using MediaPortal.Common.MediaManagement.DefaultItemAspects;
using MediaPortal.Common.MediaManagement.MLQueries;
using MediaPortal.Common.Settings;
using MediaPortal.Common.Threading;
using MediaPortal.Extensions.OnlineLibraries.Libraries.Trakt;
using MediaPortal.Extensions.OnlineLibraries.Libraries.Trakt.DataStructures;
using MediaPortal.Extensions.OnlineLibraries.Libraries.Trakt.Extension;
using MediaPortal.UiComponents.Trakt.Service;
using MediaPortal.UI.Presentation.Models;
using MediaPortal.UI.Presentation.Workflow;
using MediaPortal.UI.ServerCommunication;
using MediaPortal.Extensions.OnlineLibraries.Libraries.Trakt.Enums;
using MediaPortal.Extensions.OnlineLibraries.Libraries.Trakt.Web;
using MediaPortal.UiComponents.Trakt.Settings;

namespace MediaPortal.UiComponents.Trakt.Models
{
  public class TraktSetupModel : IWorkflowModel
  {
    #region Consts

    public const string TRAKT_SETUP_MODEL_ID_STR = "65E4F7CA-3C9C-4538-966D-2A896BFEF4D3";

    public static readonly Guid TRAKT_SETUP_MODEL_ID = new Guid(TRAKT_SETUP_MODEL_ID_STR);

    private ISettingsManager settingsManager = ServiceRegistration.Get<ISettingsManager>();
    private TraktSettings TRAKT_SETTINGS = ServiceRegistration.Get<ISettingsManager>().Load<TraktSettings>();
    
    #endregion

    #region Protected fields

    protected readonly AbstractProperty _isEnabledProperty = new WProperty(typeof(bool), false);
    protected readonly AbstractProperty _isSynchronizingProperty = new WProperty(typeof(bool), false);
    protected readonly AbstractProperty _usermameProperty = new WProperty(typeof(string), null);
    protected readonly AbstractProperty _passwordProperty = new WProperty(typeof(string), null);
    protected readonly AbstractProperty _pinCodeProperty = new WProperty(typeof(string), null);
    protected readonly AbstractProperty _testStatusProperty = new WProperty(typeof(string), string.Empty);
    protected readonly AbstractProperty _qrCodeProperty = new WProperty(typeof(string), string.Empty);

    #endregion

    #region Public properties - Bindable Data

    public AbstractProperty IsEnabledProperty
    {
      get { return _isEnabledProperty; }
    }

    public bool IsEnabled
    {
      get { return (bool)_isEnabledProperty.GetValue(); }
      set { _isEnabledProperty.SetValue(value); }
    }

    public AbstractProperty IsSynchronizingProperty
    {
      get { return _isSynchronizingProperty; }
    }

    public bool IsSynchronizing
    {
      get { return (bool)_isSynchronizingProperty.GetValue(); }
      set { _isSynchronizingProperty.SetValue(value); }
    }

    public AbstractProperty UsernameProperty
    {
      get { return _usermameProperty; }
    }

    public string Username
    {
      get { return (string)_usermameProperty.GetValue(); }
      set { _usermameProperty.SetValue(value); }
    }

    public AbstractProperty PasswordProperty
    {
      get { return _passwordProperty; }
    }

    public string Password
    {
      get { return (string)_passwordProperty.GetValue(); }
      set { _passwordProperty.SetValue(value); }
    }

    public AbstractProperty PinCodeProperty
    {
      get { return _pinCodeProperty; }
    }

    public string PinCode
    {
      get { return (string)_pinCodeProperty.GetValue(); }
      set { _pinCodeProperty.SetValue(value); }
    }

    public AbstractProperty TestStatusProperty
    {
      get { return _testStatusProperty; }
    }

    public string TestStatus
    {
      get { return (string)_testStatusProperty.GetValue(); }
      set { _testStatusProperty.SetValue(value); }
    }

    public AbstractProperty QRCodeProperty
    {
      get { return _qrCodeProperty; }
    }

    public string QRCode
    {
      get { return (string)_qrCodeProperty.GetValue(); }
      set { _qrCodeProperty.SetValue(value); }
    }


    #endregion

    #region Public methods - Commands

    /// <summary>
    /// Saves the current state to the settings file.
    /// </summary>
    public void SaveSettings()
    {
      TRAKT_SETTINGS.EnableTrakt = IsEnabled;
      TRAKT_SETTINGS.Username = Username;
      //TRAKT_SETTINGS.Password = Password;
      

      //TRAKT_SETTINGS.TraktOAuthToken = response.RefreshToken;
      //PinCode = string.Empty;
      TRAKT_SETTINGS.LastSyncActivities = TraktCache.LastSyncActivities.ToJSON().FromJSON<TraktLastSyncActivities>();


      // save user activity cache
      TraktCache.Save();
      // Save
      settingsManager.Save(TRAKT_SETTINGS);
    }

    public void SyncMediaToTrakt()
    {
      if (!IsSynchronizing)
      {

        if (!CheckAccountDetails())
          return;

        if(!Login())
          return;

        if (!TraktCache.RefreshData())
          return;

        IsSynchronizing = true;
        IThreadPool threadPool = ServiceRegistration.Get<IThreadPool>();
        threadPool.Add(SyncMediaToTrakt_Async, ThreadPriority.BelowNormal);
      }
    }

    private bool CheckAccountDetails()
    {
      if(string.IsNullOrEmpty(TRAKT_SETTINGS.TraktOAuthToken))
      {
        if (string.IsNullOrEmpty(PinCode) || PinCode.Length != 8)
        {
          //TestStatus = "Error";
          TraktLogger.Error("Trakt.tv error in credentials");
          return false;
        }
      }
      return true;
    }

    private bool Login()
    {
      TraktLogger.Info("Exchanging {0} for access-token...", PinCode.Length == 8 ? "pin-code" : "refresh-token");
      var response = TraktAPI.GetOAuthToken(PinCode.Length == 8 ? PinCode : TRAKT_SETTINGS.TraktOAuthToken);
      if (response == null || string.IsNullOrEmpty(response.AccessToken))
      {
        //TestStatus = Error
        TraktLogger.Error("Unable to login to trakt, check log for details");
        PinCode = string.Empty;
        return false;
      }

      //TestStatus = Success
      TRAKT_SETTINGS.TraktOAuthToken = response.RefreshToken;
      settingsManager.Save(TRAKT_SETTINGS);
      PinCode = string.Empty;
      TraktLogger.Info("Succes");

      return true;
    }

    public void SyncMediaToTrakt_Async()
    {
      if (SyncMovies() && SyncSeries())
      {
        TestStatus = "[Trakt.SyncFinished]";
      }
      IsSynchronizing = false;
    }

    public bool SyncMovies()
    {
      #region Get online data from cache

      #region Get unwatched / watched movies from trakt.tv
      IEnumerable<TraktMovieWatched> traktWatchedMovies = null;

      var traktUnWatchedMovies = TraktCache.GetUnWatchedMoviesFromTrakt();
      if (traktUnWatchedMovies == null)
      {
        TraktLogger.Error("Error getting unwatched movies from trakt server, unwatched and watched sync will be skipped");
      }
      else
      {
        TraktLogger.Info("There are {0} unwatched movies since the last sync with trakt.tv", traktUnWatchedMovies.Count());

        traktWatchedMovies = TraktCache.GetWatchedMoviesFromTrakt();
        if (traktWatchedMovies == null)
        {
          TraktLogger.Error("Error getting watched movies from trakt server, watched sync will be skipped");
        }
        else
        {
          TraktLogger.Info("There are {0} watched movies in trakt.tv library", traktWatchedMovies.Count().ToString());
        }
      }
      #endregion

      #region Get collected movies from trakt.tv
      var traktCollectedMovies = TraktCache.GetCollectedMoviesFromTrakt();
      if (traktCollectedMovies == null)
      {
        TraktLogger.Error("Error getting collected movies from trakt server");
      }
      else
      {
        TraktLogger.Info("There are {0} collected movies in trakt.tv library", traktCollectedMovies.Count());
      }
      #endregion

      #region Get rated movies from trakt.tv
      var traktRatedMovies = TraktCache.GetRatedMoviesFromTrakt();
      if (traktRatedMovies == null)
      {
        TraktLogger.Error("Error getting rated movies from trakt server");
      }
      else
      {
        TraktLogger.Info("There are {0} rated movies in trakt.tv library", traktRatedMovies.Count());
      }
      #endregion

      #region Get watchlisted movies from trakt.tv
      var traktWatchlistedMovies = TraktCache.GetWatchlistedMoviesFromTrakt();
      if (traktWatchlistedMovies == null)
      {
        TraktLogger.Error("Error getting watchlisted movies from trakt server");
      }
      else
      {
        TraktLogger.Info("There are {0} watchlisted movies in trakt.tv library", traktWatchlistedMovies.Count());
      }
      #endregion

      #region Get custom lists from trakt.tv
      var traktCustomLists = TraktCache.GetCustomLists();
      if (traktCustomLists == null)
      {
        TraktLogger.Error("Error getting custom lists from trakt server");
      }
      else
      {
        TraktLogger.Info("There are {0} custom lists in trakt.tv library", traktCustomLists.Count());
      }
      #endregion
      #endregion

      try
      {
        TestStatus = "[Trakt.SyncMovies]";
        Guid[] types = { MediaAspect.ASPECT_ID, MovieAspect.ASPECT_ID, VideoAspect.ASPECT_ID, ImporterAspect.ASPECT_ID };
        var contentDirectory = ServiceRegistration.Get<IServerConnectionManager>().ContentDirectory;
        if (contentDirectory == null)
        {
          TestStatus = "[Trakt.MediaLibraryNotConnected]";
          return false;
        }

        #region Get local database info

        var collectedMovies = contentDirectory.Search(new MediaItemQuery(types, null, null), true);

        TraktLogger.Info("Found {0} movies available to sync in local database", collectedMovies.Count);

        // get the movies that we have watched
        var watchedMovies = collectedMovies.Where(IsWatched).ToList();
        TraktLogger.Info("Found {0} watched movies available to sync in local database", watchedMovies.Count);

        #endregion

        #region Add movies to watched history at trakt.tv
        if (traktWatchedMovies != null)
        {
          var syncWatchedMovies = new List<TraktSyncMovieWatched>();
          TraktLogger.Info("Finding movies to add to trakt.tv watched history");

          syncWatchedMovies = (from movie in watchedMovies
                               where !traktWatchedMovies.ToList().Exists(c => MovieMatch(movie, c.Movie))
                               select new TraktSyncMovieWatched
                               {
                                 Ids = new TraktMovieId { Imdb = GetMovieImdbId(movie), Tmdb = GetMovieTmdbId(movie) },
                                 Title = GetMovieTitle(movie),
                                 Year = GetMovieYear(movie),
                                 WatchedAt = GetLastPlayedDate(movie),
                               }).ToList();

          TraktLogger.Info("Adding {0} movies to trakt.tv watched history", syncWatchedMovies.Count);

          if (syncWatchedMovies.Count > 0)
          {
            // update internal cache
            TraktCache.AddMoviesToWatchHistory(syncWatchedMovies);

            int pageSize = TRAKT_SETTINGS.SyncBatchSize;
            int pages = (int)Math.Ceiling((double)syncWatchedMovies.Count / pageSize);
            for (int i = 0; i < pages; i++)
            {
              TraktLogger.Info("Adding movies [{0}/{1}] to trakt.tv watched history", i + 1, pages);

              var pagedMovies = syncWatchedMovies.Skip(i * pageSize).Take(pageSize).ToList();

              pagedMovies.ForEach(s => TraktLogger.Info("Adding movie to trakt.tv watched history. Title = '{0}', Year = '{1}', IMDb ID = '{2}', TMDb ID = '{3}', Date Watched = '{4}'",
                                                               s.Title, s.Year.HasValue ? s.Year.ToString() : "<empty>", s.Ids.Imdb ?? "<empty>", s.Ids.Tmdb.HasValue ? s.Ids.Tmdb.ToString() : "<empty>", s.WatchedAt));

              // remove title/year such that match against online ID only
              if (TRAKT_SETTINGS.SkipMoviesWithNoIdsOnSync)
              {
                pagedMovies.ForEach(m => { m.Title = null; m.Year = null; });
              }

              var response = TraktAPI.AddMoviesToWatchedHistory(new TraktSyncMoviesWatched { Movies = pagedMovies });
              TraktLogger.LogTraktResponse<TraktSyncResponse>(response);

              // remove movies from cache which didn't succeed
              if (response != null && response.NotFound != null && response.NotFound.Movies.Count > 0)
              {
                TraktCache.RemoveMoviesFromWatchHistory(response.NotFound.Movies);
              }
            }
          }
        }
        #endregion

        #region Add movies to collection at trakt.tv
        if (traktCollectedMovies != null)
        {
          var syncCollectedMovies = new List<TraktSyncMovieCollected>();
          TraktLogger.Info("Finding movies to add to trakt.tv collection");

          syncCollectedMovies = (from movie in collectedMovies
                                 where !traktCollectedMovies.ToList().Exists(c => MovieMatch(movie, c.Movie))
                                 select new TraktSyncMovieCollected
                                 {
                                   Ids = new TraktMovieId { Imdb = GetMovieImdbId(movie), Tmdb = GetMovieTmdbId(movie) },
                                   Title = GetMovieTitle(movie),
                                   Year = GetMovieYear(movie),
                                   CollectedAt = GetDateAddedToDb(movie),
                                   MediaType = GetVideoMediaType(movie),
                                   Resolution = GetVideoResolution(movie),
                                   AudioCodec = GetVideoAudioCodec(movie),
                                   AudioChannels = "",
                                   Is3D = false
                                 }).ToList();

          TraktLogger.Info("Adding {0} movies to trakt.tv watched history", syncCollectedMovies.Count);

          if (syncCollectedMovies.Count > 0)
          {
            //update internal cache
            TraktCache.AddMoviesToCollection(syncCollectedMovies);
            int pageSize = TRAKT_SETTINGS.SyncBatchSize;
            int pages = (int)Math.Ceiling((double)syncCollectedMovies.Count / pageSize);
            for (int i = 0; i < pages; i++)
            {
              TraktLogger.Info("Adding movies [{0}/{1}] to trakt.tv collection", i + 1, pages);

              var pagedMovies = syncCollectedMovies.Skip(i * pageSize).Take(pageSize).ToList();

              pagedMovies.ForEach(s => TraktLogger.Info("Adding movie to trakt.tv collection. Title = '{0}', Year = '{1}', IMDb ID = '{2}', TMDb ID = '{3}', Date Added = '{4}', MediaType = '{5}', Resolution = '{6}', Audio Codec = '{7}', Audio Channels = '{8}'",
                                               s.Title, s.Year.HasValue ? s.Year.ToString() : "<empty>", s.Ids.Imdb ?? "<empty>", s.Ids.Tmdb.HasValue ? s.Ids.Tmdb.ToString() : "<empty>",
                                              s.CollectedAt, s.MediaType ?? "<empty>", s.Resolution ?? "<empty>", s.AudioCodec ?? "<empty>", s.AudioChannels ?? "<empty>"));

              //// remove title/year such that match against online ID only
              if (TRAKT_SETTINGS.SkipMoviesWithNoIdsOnSync)
              {
                pagedMovies.ForEach(m =>
                {
                  m.Title = null;
                  m.Year = null;
                });
              }

              var response = TraktAPI.AddMoviesToCollecton(new TraktSyncMoviesCollected { Movies = pagedMovies });
              TraktLogger.LogTraktResponse(response);

              // remove movies from cache which didn't succeed
              if (response != null && response.NotFound != null && response.NotFound.Movies.Count > 0)
              {
                TraktCache.RemoveMoviesFromCollection(response.NotFound.Movies);
              }
            }
          }
        }
        #endregion
        return true;
      }
      catch (Exception ex)
      {
        ServiceRegistration.Get<ILogger>().Error("Trakt.tv: Exception while synchronizing media library.", ex);
      }
      return false;
    }

    public bool SyncSeries()
    {

      TraktLogger.Info("Series Library Starting Sync");

      // store list of series ids so we can update the episode counts
      // of any series that syncback watched flags
      var seriesToUpdateEpisodeCounts = new HashSet<int>();

      #region Get online data from cache

      #region UnWatched / Watched

      List<TraktCache.EpisodeWatched> traktWatchedEpisodes = null;

      // get all episodes on trakt that are marked as 'unseen'
      var traktUnWatchedEpisodes = TraktCache.GetUnWatchedEpisodesFromTrakt().ToNullableList();
      if (traktUnWatchedEpisodes == null)
      {
        TraktLogger.Error("Error getting tv shows unwatched from trakt.tv server, unwatched and watched sync will be skipped");
      }
      else
      {
        TraktLogger.Info("Found {0} unwatched tv episodes in trakt.tv library", traktUnWatchedEpisodes.Count());

        // now get all episodes on trakt that are marked as 'seen' or 'watched' (this will be cached already when working out unwatched)
        traktWatchedEpisodes = TraktCache.GetWatchedEpisodesFromTrakt().ToNullableList();
        if (traktWatchedEpisodes == null)
        {
          TraktLogger.Error("Error getting tv shows watched from trakt.tv server, watched sync will be skipped");
        }
        else
        {
          TraktLogger.Info("Found {0} watched tv episodes in trakt.tv library", traktWatchedEpisodes.Count());
        }
      }

      #endregion

      #region Collection

      // get all episodes on trakt that are marked as in 'collection'
      var traktCollectedEpisodes = TraktCache.GetCollectedEpisodesFromTrakt().ToNullableList();
      if (traktCollectedEpisodes == null)
      {
        TraktLogger.Error("Error getting tv episode collection from trakt.tv server");
      }
      else
      {
        TraktLogger.Info("Found {0} tv episodes in trakt.tv collection", traktCollectedEpisodes.Count());
      }

      #endregion

      #region Ratings

      #region Episodes

      var traktRatedEpisodes = TraktCache.GetRatedEpisodesFromTrakt().ToNullableList();
      if (traktRatedEpisodes == null)
      {
        TraktLogger.Error("Error getting rated episodes from trakt.tv server");
      }
      else
      {
        TraktLogger.Info("Found {0} rated tv episodes in trakt.tv library", traktRatedEpisodes.Count());
      }

      #endregion

      #region Shows

      var traktRatedShows = TraktCache.GetRatedShowsFromTrakt().ToNullableList();
      if (traktRatedShows == null)
      {
        TraktLogger.Error("Error getting rated shows from trakt.tv server");
      }
      else
      {
        TraktLogger.Info("Found {0} rated tv shows in trakt.tv library", traktRatedShows.Count());
      }

      #endregion

      #region Seasons

      var traktRatedSeasons = TraktCache.GetRatedSeasonsFromTrakt().ToNullableList();
      if (traktRatedSeasons == null)
      {
        TraktLogger.Error("Error getting rated seasons from trakt.tv server");
      }
      else
      {
        TraktLogger.Info("Found {0} rated tv seasons in trakt.tv library", traktRatedSeasons.Count());
      }

      #endregion

      #endregion

      #region Watchlist

      #region Shows

      var traktWatchlistedShows = TraktCache.GetWatchlistedShowsFromTrakt();
      if (traktWatchlistedShows == null)
      {
        TraktLogger.Error("Error getting watchlisted shows from trakt.tv server");
      }
      else
      {
        TraktLogger.Info("Found {0} watchlisted tv shows in trakt.tv library", traktWatchlistedShows.Count());
      }

      #endregion

      #region Seasons

      var traktWatchlistedSeasons = TraktCache.GetWatchlistedSeasonsFromTrakt();
      if (traktWatchlistedSeasons == null)
      {
        TraktLogger.Error("Error getting watchlisted seasons from trakt.tv server");
      }
      else
      {
        TraktLogger.Info("Found {0} watchlisted tv seasons in trakt.tv library", traktWatchlistedSeasons.Count());
      }

      #endregion

      #region Episodes

      var traktWatchlistedEpisodes = TraktCache.GetWatchlistedEpisodesFromTrakt();
      if (traktWatchlistedEpisodes == null)
      {
        TraktLogger.Error("Error getting watchlisted episodes from trakt.tv server");
      }
      else
      {
        TraktLogger.Info("Found {0} watchlisted tv episodes in trakt.tv library", traktWatchlistedEpisodes.Count());
      }

      #endregion

      #endregion

      #endregion

      if (traktCollectedEpisodes != null)
      {
        try
        {
          TestStatus = "[Trakt.SyncSeries]";
          Guid[] types = { MediaAspect.ASPECT_ID, SeriesAspect.ASPECT_ID, VideoAspect.ASPECT_ID, ImporterAspect.ASPECT_ID};
          MediaItemQuery mediaItemQuery = new MediaItemQuery(types, null, null);
          var contentDirectory = ServiceRegistration.Get<IServerConnectionManager>().ContentDirectory;
          if (contentDirectory == null)
          {
            TestStatus = "[Trakt.MediaLibraryNotConnected]";
            return false;
          }

          #region Get data from local database

          var localEpisodes = contentDirectory.Search(mediaItemQuery, true);
          int episodeCount = localEpisodes.Count;

          TraktLogger.Info("Found {0} total episodes in local database", episodeCount);

          // get the episodes that we have watched
          var localWatchedEpisodes = localEpisodes.Where(IsWatched).ToList();

          TraktLogger.Info("Found {0} episodes watched in tvseries database", localWatchedEpisodes.Count);

          #endregion

          #region Add episodes to watched history at trakt.tv
          int showCount = 0;
          int iSyncCounter = 0;
          if (traktWatchedEpisodes != null)
          {
            var syncWatchedShows = GetWatchedShowsForSyncEx(localWatchedEpisodes, traktWatchedEpisodes);

            TraktLogger.Info("Found {0} local tv show(s) with {1} watched episode(s) to add to trakt.tv watched history", syncWatchedShows.Shows.Count, syncWatchedShows.Shows.Sum(sh => sh.Seasons.Sum(se => se.Episodes.Count())));

            showCount = syncWatchedShows.Shows.Count;
            foreach (var show in syncWatchedShows.Shows)
            {
              int showEpisodeCount = show.Seasons.Sum(s => s.Episodes.Count());
              TraktLogger.Info("Adding tv show [{0}/{1}] to trakt.tv episode watched history, Episode Count = '{2}', Show Title = '{3}', Show Year = '{4}', Show TVDb ID = '{5}', Show IMDb ID = '{6}'",
                                  ++iSyncCounter, showCount, showEpisodeCount, show.Title, show.Year.HasValue ? show.Year.ToString() : "<empty>", show.Ids.Tvdb, show.Ids.Imdb ?? "<empty>");

              show.Seasons.ForEach(s => s.Episodes.ForEach(e =>
              {
                TraktLogger.Info("Adding episode to trakt.tv watched history, Title = '{0} - {1}x{2}', Watched At = '{3}'", show.Title, s.Number, e.Number, e.WatchedAt.ToLogString());
              }));

              // only sync one show at a time regardless of batch size in settings
              var pagedShows = new List<TraktSyncShowWatchedEx>();
              pagedShows.Add(show);

              var response = TraktAPI.AddShowsToWatchedHistoryEx(new TraktSyncShowsWatchedEx { Shows = pagedShows });
              TraktLogger.LogTraktResponse<TraktSyncResponse>(response);

              // only add to cache if it was a success
              // note: we don't get back the same object type so makes it hard to figure out what failed
              if (response != null && response.Added != null && response.Added.Episodes == showEpisodeCount)
              {
                // update local cache
                TraktCache.AddEpisodesToWatchHistory(show);
              }
            }
          }
          #endregion

          #region Add episodes to collection at trakt.tv

          if (traktCollectedEpisodes != null)
          {
            var syncCollectedShows = GetCollectedShowsForSyncEx(localEpisodes, traktCollectedEpisodes);

            TraktLogger.Info("Found {0} local tv show(s) with {1} collected episode(s) to add to trakt.tv collection", syncCollectedShows.Shows.Count, syncCollectedShows.Shows.Sum(sh => sh.Seasons.Sum(se => se.Episodes.Count())));

            iSyncCounter = 0;
            showCount = syncCollectedShows.Shows.Count;
            foreach (var show in syncCollectedShows.Shows)
            {
              int showEpisodeCount = show.Seasons.Sum(s => s.Episodes.Count());
              TraktLogger.Info("Adding tv show [{0}/{1}] to trakt.tv episode collection, Episode Count = '{2}', Show Title = '{3}', Show Year = '{4}', Show TVDb ID = '{5}', Show IMDb ID = '{6}'",
                ++iSyncCounter, showCount, showEpisodeCount, show.Title, show.Year.HasValue ? show.Year.ToString() : "<empty>", show.Ids.Tvdb, show.Ids.Imdb ?? "<empty>");

              show.Seasons.ForEach(s => s.Episodes.ForEach(e =>
              {
                TraktLogger.Info("Adding episode to trakt.tv collection, Title = '{0} - {1}x{2}', Collected At = '{3}', Audio Channels = '{4}', Audio Codec = '{5}', Resolution = '{6}', Media Type = '{7}', Is 3D = '{8}'", show.Title, s.Number, e.Number, e.CollectedAt.ToLogString(), e.AudioChannels.ToLogString(), e.AudioCodec.ToLogString(), e.Resolution.ToLogString(), e.MediaType.ToLogString(), e.Is3D);
              }));

              // only sync one show at a time regardless of batch size in settings
              var pagedShows = new List<TraktSyncShowCollectedEx>();
              pagedShows.Add(show);

              var response = TraktAPI.AddShowsToCollectonEx(new TraktSyncShowsCollectedEx { Shows = pagedShows });
              TraktLogger.LogTraktResponse<TraktSyncResponse>(response);

              // only add to cache if it was a success
              if (response != null && response.Added != null && response.Added.Episodes == showEpisodeCount)
              {
                // update local cache
                TraktCache.AddEpisodesToCollection(show);
              }
            }
          }
          #endregion
          return true;
        }
        catch (Exception ex)
        {
          ServiceRegistration.Get<ILogger>().Error("Trakt.tv: Exception while synchronizing media library.", ex);
        }
      }
      return false;
    }

    /// <summary>
    /// Returns a list of shows for collection sync as show objects with season / episode hierarchy
    /// </summary>
    private TraktSyncShowsCollectedEx GetCollectedShowsForSyncEx(IList<MediaItem> localCollectedEpisodes, List<TraktCache.EpisodeCollected> traktEpisodesCollected)
    {
      TraktLogger.Info("Finding local episodes to add to trakt.tv collection");

      // prepare new sync object
      var syncCollectedEpisodes = new TraktSyncShowsCollectedEx();
      syncCollectedEpisodes.Shows = new List<TraktSyncShowCollectedEx>();

      // create a unique key to lookup and search for faster
      var onlineEpisodes = traktEpisodesCollected.ToLookup(tce => CreateLookupKey(tce), tce => tce);

      foreach (var episode in localCollectedEpisodes)
      {
        string tvdbKey = CreateLookupKey(episode);

        var traktEpisode = onlineEpisodes[tvdbKey].FirstOrDefault();

        // check if not collected on trakt and add it to sync list
        if (traktEpisode == null)
        {
          // check if we already have the show added to our sync object
          var syncShow = syncCollectedEpisodes.Shows.FirstOrDefault(sce => sce.Ids != null && sce.Ids.Tvdb == GetSeriesTvdbId(episode));
          if (syncShow == null)
          {
            // get show data from episode
            var show = GetSeriesTvdbId(episode);
            if (show == 0) continue;

            // create new show
            syncShow = new TraktSyncShowCollectedEx
            {
              Ids = new TraktShowId
              {
                Tvdb = GetSeriesTvdbId(episode),
                Imdb = GetSeriesImdbId(episode)
              },
              Title = GetSeriesTitle(episode),
             // Year = GetSeriesTitleAndYear(episode, )
            };

            // add a new season collection to show object
            syncShow.Seasons = new List<TraktSyncShowCollectedEx.Season>();

            // add show to the collection
            syncCollectedEpisodes.Shows.Add(syncShow);
          }

          // check if season exists in show sync object
          var syncSeason = syncShow.Seasons.FirstOrDefault(ss => ss.Number == GetSeasonIndex(episode));
          if (syncSeason == null)
          {
            // create new season
            syncSeason = new TraktSyncShowCollectedEx.Season
            {
              Number = GetSeasonIndex(episode)
            };

            // add a new episode collection to season object
            syncSeason.Episodes = new List<TraktSyncShowCollectedEx.Season.Episode>();

            // add season to the show
            syncShow.Seasons.Add(syncSeason);
          }

          // add episode to season
          syncSeason.Episodes.Add(new TraktSyncShowCollectedEx.Season.Episode
          {
            Number = GetEpisodeIndex(episode),
            CollectedAt =  GetDateAddedToDb(episode),
            MediaType = GetVideoMediaType(episode),
            Resolution = GetVideoResolution(episode),
            AudioCodec = GetVideoAudioCodec(episode),
            AudioChannels = "",
            Is3D = false
          });
        }
      }
      return syncCollectedEpisodes;
    }

    /// <summary>
    /// Returns a list of shows for watched history sync as show objects with season / episode hierarchy
    /// </summary>
    private TraktSyncShowsWatchedEx GetWatchedShowsForSyncEx(IList<MediaItem> localWatchedEpisodes, List<TraktCache.EpisodeWatched> traktEpisodesWatched)
    {
      TraktLogger.Info("Finding local episodes to add to trakt.tv watched history");

      // prepare new sync object
      var syncWatchedEpisodes = new TraktSyncShowsWatchedEx();
      syncWatchedEpisodes.Shows = new List<TraktSyncShowWatchedEx>();

      // create a unique key to lookup and search for faster
      var onlineEpisodes = traktEpisodesWatched.ToLookup(twe => CreateLookupKey(twe), twe => twe);

      foreach (var episode in localWatchedEpisodes)
      {
        string tvdbKey = CreateLookupKey(episode);

        var traktEpisode = onlineEpisodes[tvdbKey].FirstOrDefault();

        // check if not watched on trakt and add it to sync list
        if (traktEpisode == null)
        {
          // check if we already have the show added to our sync object
          var syncShow = syncWatchedEpisodes.Shows.FirstOrDefault(swe => swe.Ids != null && swe.Ids.Tvdb == GetSeriesTvdbId(episode));
          if (syncShow == null)
          {
            // get show data from episode
            var show = GetSeriesTvdbId(episode);
            if (show == 0) continue;

            // create new show
            syncShow = new TraktSyncShowWatchedEx
            {
              Ids = new TraktShowId
              {
                Tvdb = GetSeriesTvdbId(episode),
                Imdb = GetSeriesImdbId(episode)
              },
              Title = GetSeriesTitle(episode),
              //Year = show.Year.ToNullableInt32()
            };

            // add a new season collection to show object
            syncShow.Seasons = new List<TraktSyncShowWatchedEx.Season>();

            // add show to the collection
            syncWatchedEpisodes.Shows.Add(syncShow);
          }

          // check if season exists in show sync object
          var syncSeason = syncShow.Seasons.FirstOrDefault(ss => ss.Number == GetSeasonIndex(episode));
          if (syncSeason == null)
          {
            // create new season
            syncSeason = new TraktSyncShowWatchedEx.Season
            {
              Number = GetSeasonIndex(episode)
            };

            // add a new episode collection to season object
            syncSeason.Episodes = new List<TraktSyncShowWatchedEx.Season.Episode>();

            // add season to the show
            syncShow.Seasons.Add(syncSeason);
          }

          // add episode to season
          syncSeason.Episodes.Add(new TraktSyncShowWatchedEx.Season.Episode
          {
            Number = GetEpisodeIndex(episode),
            WatchedAt = GetLastPlayedDate(episode)
          });
        }
      }

      return syncWatchedEpisodes;
    }

    private static bool IsWatched(MediaItem mediaItem)
    {
      int playCount;
      return (MediaItemAspect.TryGetAttribute(mediaItem.Aspects, MediaAspect.ATTR_PLAYCOUNT, 0, out playCount) && playCount > 0);
    }

    private string GetMovieImdbId(MediaItem mediaItem)
    {
      string imdb;
      if (MediaItemAspect.TryGetAttribute(mediaItem.Aspects, MovieAspect.ATTR_IMDB_ID, out imdb) && !string.IsNullOrWhiteSpace(imdb))
        return imdb;
      return "";
    }

    private int GetMovieTmdbId(MediaItem mediaItem)
    {
      int tmdb;
      if (MediaItemAspect.TryGetAttribute(mediaItem.Aspects, MovieAspect.ATTR_TMDB_ID, out tmdb) && tmdb > 0)
        return tmdb;
      return 0;
    }

    private int GetMovieYear(MediaItem mediaItem)
    {
      DateTime dtValue;
      if (MediaItemAspect.TryGetAttribute(mediaItem.Aspects, MediaAspect.ATTR_RECORDINGTIME, out dtValue))
       return dtValue.Year;

      return 0;
    }

    private string GetMovieTitle(MediaItem mediaItem)
    {
      string value;
      if (MediaItemAspect.TryGetAttribute(mediaItem.Aspects, MovieAspect.ATTR_MOVIE_NAME, out value) && !string.IsNullOrWhiteSpace(value))
        return value;

      return "";
    }

    private string GetDateAddedToDb(MediaItem mediaItem)
    {
      DateTime addedToDb;
      if (MediaItemAspect.TryGetAttribute(mediaItem.Aspects, ImporterAspect.ATTR_DATEADDED, out addedToDb))
        return addedToDb.ToUniversalTime().ToISO8601();
      return "";
    }

    private string GetLastPlayedDate(MediaItem mediaItem)
    {
      DateTime lastplayed;
      if (MediaItemAspect.TryGetAttribute(mediaItem.Aspects, MediaAspect.ATTR_LASTPLAYED, out lastplayed))
        return lastplayed.ToUniversalTime().ToISO8601();
      return "";
    }

    /// <summary>
    /// Gets the trakt compatible string for the movies Audio
    /// </summary>
    private string GetVideoAudioCodec(MediaItem mediaItem)
    {
      string audioCodec;

      if (MediaItemAspect.TryGetAttribute(mediaItem.Aspects, VideoAspect.ATTR_AUDIOENCODING, out audioCodec) && !string.IsNullOrWhiteSpace(audioCodec))
      {
        switch (audioCodec.ToLowerInvariant())
        {
          case "truehd":
            return TraktAudio.dolby_truehd.ToString();
          case "dts":
            return TraktAudio.dts.ToString();
          case "dtshd":
            return TraktAudio.dts_ma.ToString();
          case "ac3":
            return TraktAudio.dolby_digital.ToString();
          case "aac":
            return TraktAudio.aac.ToString();
          case "mp2":
            return TraktAudio.mp3.ToString();
          case "pcm":
            return TraktAudio.lpcm.ToString();
          case "ogg":
            return TraktAudio.ogg.ToString();
          case "wma":
            return TraktAudio.wma.ToString();
          case "flac":
            return TraktAudio.flac.ToString();
          default:
            return null;
        }
      }
      return null;
    }

    /// <summary>
    /// Gets the trakt compatible string for the movies Media Type
    /// </summary>
    private string GetVideoMediaType(MediaItem mediaItem)
    {
      bool isDvd;

      MediaItemAspect.TryGetAttribute(mediaItem.Aspects, VideoAspect.ATTR_ISDVD, out isDvd);

      if (isDvd)
        return TraktMediaType.dvd.ToString();

      return TraktMediaType.digital.ToString();
    }

    /// <summary>
    /// Checks if a local movie is the same as an online movie
    /// </summary>
    private bool MovieMatch(MediaItem localMovie, TraktMovie traktMovie)
    {
      // IMDb comparison
      if (!string.IsNullOrEmpty(traktMovie.Ids.Imdb) && !string.IsNullOrEmpty(GetMovieImdbId(localMovie)))
      {
        return String.Compare(GetMovieImdbId(localMovie), traktMovie.Ids.Imdb, StringComparison.OrdinalIgnoreCase) == 0;
      }

      // TMDb comparison
      if ((GetMovieTmdbId(localMovie) != 0) && traktMovie.Ids.Tmdb.HasValue)
      {
        return GetMovieTmdbId(localMovie) == traktMovie.Ids.Tmdb.Value;
      }

      // Title & Year comparison
      {
        return string.Compare(GetMovieTitle(localMovie), traktMovie.Title, true) == 0 && (GetMovieYear(localMovie) == traktMovie.Year);
      }
    }

    /// <summary>
    /// Gets the trakt compatible string for the movies Resolution
    /// </summary>
    private string GetVideoResolution(MediaItem mediaItem)
    {
      int width;

      if (MediaItemAspect.TryGetAttribute(mediaItem.Aspects, VideoAspect.ATTR_WIDTH, out width) && width > 0)

        switch (width)
        {
          case 1920:
            return TraktResolution.hd_1080p.ToString();
          case 1280:
            return TraktResolution.hd_720p.ToString();
          case 720:
            return TraktResolution.sd_576p.ToString();
          case 640:
            return TraktResolution.sd_480p.ToString();
          case 2160:
            return TraktResolution.uhd_4k.ToString();
          default:
            return TraktResolution.hd_720p.ToString();
        }

      return null;
    }

    private string GetSeriesTitle(MediaItem mediaItem)
    {
      string value;
      return MediaItemAspect.TryGetAttribute(mediaItem.Aspects, SeriesAspect.ATTR_SERIESNAME, out value) ? value : null;
    }

    private int GetSeriesTvdbId(MediaItem mediaItem)
    {
      int value;
      return MediaItemAspect.TryGetAttribute(mediaItem.Aspects, SeriesAspect.ATTR_TVDB_ID, out value) ? value : 0;
    }

    private int GetSeasonIndex(MediaItem mediaItem)
    {
      int value;
      return MediaItemAspect.TryGetAttribute(mediaItem.Aspects, SeriesAspect.ATTR_SEASON, out value) ? value : 0;
    }

    private int GetEpisodeIndex(MediaItem mediaItem)
    {
      List<int> intList;
      if (MediaItemAspect.TryGetAttribute(mediaItem.Aspects, SeriesAspect.ATTR_EPISODE, out intList) && intList.Any())
        return intList.First(); // TODO: multi episode files?!

      return intList.FirstOrDefault();
    }

    private string GetSeriesImdbId(MediaItem mediaItem)
    {
      string value;
      return MediaItemAspect.TryGetAttribute(mediaItem.Aspects, SeriesAspect.ATTR_IMDB_ID, out value) ? value : null;
    }

    private string CreateLookupKey(MediaItem episode)
    {
      var tvdid = GetSeriesTvdbId(episode);
      var seasonIndex = GetSeasonIndex(episode);
      var episodeIndex = GetEpisodeIndex(episode);
      return string.Format("{0}_{1}_{2}", tvdid, seasonIndex, episodeIndex);

    }

    private string CreateLookupKey(TraktCache.Episode episode)
    {
      string show;

      if (episode.ShowTvdbId != null)
      {
        show = episode.ShowTvdbId.Value.ToString();
      }
      else if (episode.ShowImdbId != null)
      {
        show = episode.ShowImdbId;
      }
      else
      {
        if (episode.ShowTitle == null)
          return episode.GetHashCode().ToString();

        show = episode.ShowTitle + "_" + episode.ShowYear ?? string.Empty;
      }

      return string.Format("{0}_{1}_{2}", show, episode.Season, episode.Number);
    }

    #endregion

    #region IWorkflowModel implementation

    public Guid ModelId
    {
      get { return TRAKT_SETUP_MODEL_ID; }
    }

    public bool CanEnterState(NavigationContext oldContext, NavigationContext newContext)
    {
      return true;
    }
   
    public void EnterModelContext(NavigationContext oldContext, NavigationContext newContext)
    {


      // Load settings
      IsEnabled = TRAKT_SETTINGS.EnableTrakt;
      //Username = TRAKT_SETTINGS.Username;
      //Password = TRAKT_SETTINGS.Password;
      PinCode = string.Empty;

      // initialise API settings
      //TraktAPI.ApplicationId = TRAKT_SETTINGS.ApplicationId;
      //TraktAPI.UserAgent = TRAKT_SETTINGS.UserAgent;
      //TraktAPI.UseSSL = TRAKT_SETTINGS.UseSSL;

      //var account = new TraktAuthentication
      //{
      //  Username = TRAKT_SETTINGS.Username,
      //  Password = TRAKT_SETTINGS.Password
      //};

      //if (!account.Password.Equals("") || !account.Username.Equals(""))
      //{
      //  var response = TraktAPI.Login(account.ToJSON());
      //  if (response == null || string.IsNullOrEmpty(response.Token))
      //  {
      //    TestStatus = "Failed login using saved credantials";
      //  }
      //  else
      //  {
      //    TestStatus = "Successfully logged into Trakt.tv";
      //    // save token
      //    TraktAPI.UserToken = response.Token;
      //    TraktAPI.Username = TRAKT_SETTINGS.Username;
      //    TraktAPI.Password = TRAKT_SETTINGS.Password;
      //    TRAKT_SETTINGS.AccountStatus = ConnectionState.Connected;
      //    if (TRAKT_SETTINGS.UserLogins == null)
      //    {
      //      TRAKT_SETTINGS.UserLogins = new List<TraktAuthentication> { new TraktAuthentication { Username = Username, Password = Password } };
      //    }
      //  }
      //}

      //  var response = TraktAPI.GetOAuthToken("");
   //  QRCode = "C:\\Users\\adrian\\Documents\\GitHub\\qr.png";


      



      // initialise the last sync activities 
      if (TRAKT_SETTINGS.LastSyncActivities == null) TRAKT_SETTINGS.LastSyncActivities = new TraktLastSyncActivities();
    }

    public void ExitModelContext(NavigationContext oldContext, NavigationContext newContext)
    {
      // Nothing to do here
    }

    public void ChangeModelContext(NavigationContext oldContext, NavigationContext newContext, bool push)
    {
      // Nothing to do here
    }

    public void Deactivate(NavigationContext oldContext, NavigationContext newContext)
    {
      // Nothing to do here
    }

    public void Reactivate(NavigationContext oldContext, NavigationContext newContext)
    {
      // Nothing to do here
    }

    public void UpdateMenuActions(NavigationContext context, IDictionary<Guid, WorkflowAction> actions)
    {
      // Nothing to do here
    }

    public ScreenUpdateMode UpdateScreen(NavigationContext context, ref string screen)
    {
      return ScreenUpdateMode.AutoWorkflowManager;
    }

    #endregion
  }
}
