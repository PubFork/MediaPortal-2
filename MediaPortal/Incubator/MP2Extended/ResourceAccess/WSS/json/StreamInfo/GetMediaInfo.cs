﻿using System;
using System.Collections.Generic;
using HttpServer;
using HttpServer.Exceptions;
using MediaPortal.Common;
using MediaPortal.Common.Logging;
using MediaPortal.Common.MediaManagement;
using MediaPortal.Common.MediaManagement.DefaultItemAspects;
using MediaPortal.Plugins.MP2Extended.MAS.General;
using MediaPortal.Plugins.MP2Extended.WSS.StreamInfo;

namespace MediaPortal.Plugins.MP2Extended.ResourceAccess.WSS.json.StreamInfo
{
  // TODO: don't really know what the pupose of this method is.
  class GetMediaInfo : IRequestMicroModuleHandler
  {
    public dynamic Process(IHttpRequest request)
    {
      Logger.Info("MAS-GetMediaItem: AbsolutePath: {0}, uriParts.Length: {1}, Lastpart: {2}", request.Uri.AbsolutePath);

      HttpParam httpParam = request.Param;
      string id = httpParam["id"].Value;
      if (id == null)
        throw new BadRequestException("GetMediaItem: no id is null");

      ISet<Guid> necessaryMIATypes = new HashSet<Guid>();
      necessaryMIATypes.Add(MediaAspect.ASPECT_ID);
      necessaryMIATypes.Add(ProviderResourceAspect.ASPECT_ID);
      necessaryMIATypes.Add(ImporterAspect.ASPECT_ID);

      ISet<Guid> optionalMIATypes = new HashSet<Guid>();
      optionalMIATypes.Add(VideoAspect.ASPECT_ID);
      optionalMIATypes.Add(AudioAspect.ASPECT_ID);
      optionalMIATypes.Add(ImageAspect.ASPECT_ID);

      MediaItem item = GetMediaItems.GetMediaItemById(httpParam["id"].Value, necessaryMIATypes);

      if (item == null)
        throw new BadRequestException(String.Format("GetMediaInfo: No MediaItem found with id: {0}", httpParam["id"].Value));

      long duration = 0;
      string container = string.Empty;
      List<WebVideoStream> webVideoStreams = new List<WebVideoStream>();
      List<WebAudioStream> webAudioStreams = new List<WebAudioStream>();
      List<WebSubtitleStream> webSubtitleStreams = new List<WebSubtitleStream>();
      
      // decide which type of media item we have
      if (item.Aspects.ContainsKey(VideoAspect.ASPECT_ID))
      {
        var videoAspect = item.Aspects[VideoAspect.ASPECT_ID];
        duration = (long)videoAspect[VideoAspect.ATTR_DURATION];
        //container not in DB
        
        // Video Stream
        WebVideoStream webVideoStream = new WebVideoStream();
        webVideoStream.Codec = (string)videoAspect[VideoAspect.ATTR_VIDEOENCODING];
        //webVideoStream.DisplayAspectRatio;
        //webVideoStream.DisplayAspectRatioString;
        webVideoStream.Height = (int)videoAspect[VideoAspect.ATTR_HEIGHT];
        //webVideoStream.ID;
        //webVideoStream.Index;
        //webVideoStream.Interlaced;
        webVideoStream.Width = (int)videoAspect[VideoAspect.ATTR_WIDTH];
        
        webVideoStreams.Add(webVideoStream);

        // Audio streams
        for (int i = 0; i < (int)videoAspect[VideoAspect.ATTR_AUDIOSTREAMCOUNT]; i++)
        {
          WebAudioStream webAudioStream = new WebAudioStream();
          //webAudioStream.Channels = ;
          webAudioStream.Codec = (string)videoAspect[VideoAspect.ATTR_AUDIOENCODING];
          //webAudioStream.ID;
          //webAudioStream.Index;
          //webAudioStream.Language;
          //webAudioStream.LanguageFull;
          //webAudioStream.Title;

          webAudioStreams.Add(webAudioStream);
        }

        // no subtitle information in DB
      }
      if (item.Aspects.ContainsKey(AudioAspect.ASPECT_ID))
      {
        var audiAspect = item.Aspects[AudioAspect.ASPECT_ID];
        duration = (long)audiAspect[AudioAspect.ATTR_DURATION];
        //container not in DB
      }
      if (item.Aspects.ContainsKey(ImageAspect.ASPECT_ID))
      {
        var imageAspect = item.Aspects[ImageAspect.ASPECT_ID];
        //container not in DB
      }

      WebMediaInfo webMediaInfo = new WebMediaInfo
      {
        Duration = duration,
        Container = container,
        VideoStreams = webVideoStreams,
        AudioStreams = webAudioStreams,
        SubtitleStreams = webSubtitleStreams
      };


      return webMediaInfo;
    }

    internal static ILogger Logger
    {
      get { return ServiceRegistration.Get<ILogger>(); }
    }
  }
}
