﻿#region Copyright (C) 2007-2017 Team MediaPortal

/*
    Copyright (C) 2007-2017 Team MediaPortal
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

using MediaPortal.Common.MediaManagement.DefaultItemAspects;
using MediaPortal.Common.MediaManagement.MLQueries;
using MediaPortal.UiComponents.Media.General;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MediaPortal.UiComponents.Media.FilterCriteria
{
  public class FilterByAlbumCriterion : RelationshipMLFilterCriterion
  {
    public FilterByAlbumCriterion() :
      base(AudioAlbumAspect.ROLE_ALBUM, AudioAspect.ROLE_TRACK, Consts.NECESSARY_ALBUM_MIAS, Consts.OPTIONAL_ALBUM_MIAS,
        new AttributeSortInformation(AudioAlbumAspect.ATTR_ALBUM, SortDirection.Ascending))
    {
    }

    protected override IFilter CreateQueryFilter(IEnumerable<Guid> necessaryMIATypeIds, IFilter filter, bool showVirtual)
    {
      //If previous filter is an album filter we can just use it directly
      if (filter != null && necessaryMIATypeIds != null && necessaryMIATypeIds.Contains(AudioAlbumAspect.ASPECT_ID))
        return filter;
      //else we'll use the default relationship filter
      return base.CreateQueryFilter(necessaryMIATypeIds, filter, showVirtual);
    }
  }
}
