﻿using System;
using System.Collections.Generic;
using System.Linq;
using HttpServer;
using HttpServer.Exceptions;
using MediaPortal.Common;
using MediaPortal.Common.Logging;
using MediaPortal.Common.MediaManagement;
using MediaPortal.Common.MediaManagement.DefaultItemAspects;
using MediaPortal.Plugins.MP2Extended.Common;
using MediaPortal.Plugins.MP2Extended.Extensions;
using MediaPortal.Plugins.MP2Extended.MAS;
using MediaPortal.Plugins.MP2Extended.MAS.General;
using MediaPortal.Plugins.MP2Extended.MAS.Movie;
using Newtonsoft.Json;

namespace MediaPortal.Plugins.MP2Extended.ResourceAccess.MAS.Movie
{
  class GetMoviesDetailedByRange : IRequestMicroModuleHandler
  {
    public dynamic Process(IHttpRequest request)
    {
      HttpParam httpParam = request.Param;
      string start = httpParam["start"].Value;
      string end = httpParam["end"].Value;

      Logger.Info("GetMoviesDetailedByRange: start: {0}, end: {1}", start, end);

      if (start == null || end == null)
        throw new BadRequestException("start or end parameter is missing");

      int startInt;
      if (!Int32.TryParse(start, out startInt))
      {
        throw new BadRequestException(String.Format("GetMoviesDetailedByRange: Couldn't convert start to int: {0}", start));
      }

      int endInt;
      if (!Int32.TryParse(end, out endInt))
      {
        throw new BadRequestException(String.Format("GetMoviesDetailedByRange: Couldn't convert end to int: {0}", end));
      }
      
      ISet<Guid> necessaryMIATypes = new HashSet<Guid>();
      necessaryMIATypes.Add(MediaAspect.ASPECT_ID);
      necessaryMIATypes.Add(ProviderResourceAspect.ASPECT_ID);
      necessaryMIATypes.Add(ImporterAspect.ASPECT_ID);
      necessaryMIATypes.Add(VideoAspect.ASPECT_ID);
      necessaryMIATypes.Add(MovieAspect.ASPECT_ID);

      IList<MediaItem> items = GetMediaItems.GetMediaItemsByAspect(necessaryMIATypes);

      if (items.Count == 0)
        throw new BadRequestException("No Tv Episodes found");

      var output = new List<WebMovieDetailed>();

      foreach (var item in items)
      {

        MediaItemAspect movieAspects = item.Aspects[MovieAspect.ASPECT_ID];

        WebMovieDetailed webMovieDetailed = new WebMovieDetailed();
        var TMDBId = movieAspects[MovieAspect.ATTR_TMDB_ID];
        if (TMDBId != null)
        {
          webMovieDetailed.ExternalId.Add(new WebExternalId
          {
            Site = "TMDB",
            Id = ((int)TMDBId).ToString()
          });
        }
        var ImdbId = movieAspects[MovieAspect.ATTR_IMDB_ID];
        if (ImdbId != null)
        {
          webMovieDetailed.ExternalId.Add(new WebExternalId
          {
            Site = "IMDB",
            Id = (string)movieAspects[MovieAspect.ATTR_IMDB_ID]
          });
        }

        webMovieDetailed.IsProtected = false; //??
        webMovieDetailed.Rating = Convert.ToSingle((double)movieAspects[MovieAspect.ATTR_TOTAL_RATING]);
        webMovieDetailed.Type = WebMediaType.Movie;
        webMovieDetailed.Watched = ((int)(item.Aspects[MediaAspect.ASPECT_ID][MediaAspect.ATTR_PLAYCOUNT] ?? 0) > 0);
        //webMovieDetailed.Path = ;
        //webMovieDetailed.Artwork = ;
        //webMovieDetailed.Year = ;
        //webMovieDetailed.Language = ;
        webMovieDetailed.Runtime = (int)movieAspects[MovieAspect.ATTR_RUNTIME_M];
        webMovieDetailed.Tagline = (string)movieAspects[MovieAspect.ATTR_TAGLINE];
        webMovieDetailed.DateAdded = (DateTime)item.Aspects[ImporterAspect.ASPECT_ID][ImporterAspect.ATTR_DATEADDED];
        webMovieDetailed.Id = item.MediaItemId.ToString();
        webMovieDetailed.PID = 0;
        webMovieDetailed.Title = (string)item[MediaAspect.ASPECT_ID][MediaAspect.ATTR_TITLE];
        webMovieDetailed.Summary = (string)item[VideoAspect.ASPECT_ID][VideoAspect.ATTR_STORYPLOT];
        var videoWriters = (HashSet<object>)item[VideoAspect.ASPECT_ID][VideoAspect.ATTR_WRITERS];
        if (videoWriters != null)
          webMovieDetailed.Writers = videoWriters.Cast<string>().ToList();
        var videoDirectors = (HashSet<object>)item[VideoAspect.ASPECT_ID][VideoAspect.ATTR_DIRECTORS];
        if (videoDirectors != null)
          webMovieDetailed.Directors = videoDirectors.Cast<string>().ToList();
        var movieGenres = (HashSet<object>)item[VideoAspect.ASPECT_ID][VideoAspect.ATTR_GENRES];
        if (movieGenres != null)
          webMovieDetailed.Genres = movieGenres.Cast<string>().ToList();
        var movieActors = (HashSet<object>)item[VideoAspect.ASPECT_ID][VideoAspect.ATTR_ACTORS];
        if (movieActors != null)
        {
          webMovieDetailed.Actors = new List<WebActor>();
          foreach (var actor in movieActors)
          {
            webMovieDetailed.Actors.Add(new WebActor
            {
              Title = actor.ToString(),
              PID = 0
            });
          }
        }

        output.Add(webMovieDetailed);
      }

      // sort and filter
      string sort = httpParam["sort"].Value;
      string order = httpParam["order"].Value;
      string filter = httpParam["filter"].Value;
      if (sort != null && order != null)
      {
        WebSortField webSortField = (WebSortField)JsonConvert.DeserializeObject(sort, typeof(WebSortField));
        WebSortOrder webSortOrder = (WebSortOrder)JsonConvert.DeserializeObject(order, typeof(WebSortOrder));

        output = output.Filter(filter).SortWebMovieDetailed(webSortField, webSortOrder).ToList();
      }
      else
        output = output.Filter(filter).ToList();

      // get range
      if (startInt > output.Count - 1)
        startInt = output.Count - 1;

      if (endInt > output.Count - 1)
        endInt = output.Count - 1;

      if ((endInt - startInt) < 0)
        throw new BadRequestException(String.Format("Invalid range: {0}", (endInt - startInt)));

      int count = (endInt - startInt) + 1;

      output = output.GetRange(startInt, count);

      return output;
    }

    internal static ILogger Logger
    {
      get { return ServiceRegistration.Get<ILogger>(); }
    }
  }
}
