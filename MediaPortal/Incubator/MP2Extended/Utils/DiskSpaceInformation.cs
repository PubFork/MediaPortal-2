﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using MediaPortal.Common;
using MediaPortal.Common.Logging;
using MediaPortal.Plugins.MP2Extended.TAS.Tv;

namespace MediaPortal.Plugins.MP2Extended.Utils
{
  internal static class DiskSpaceInformation
  {
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string lpDirectoryName,
       out ulong lpFreeBytesAvailable,
       out ulong lpTotalNumberOfBytes,
       out ulong lpTotalNumberOfFreeBytes);

    public static WebDiskSpaceInformation GetSpaceInformation(string directory)
    {
      ulong freeBytes, totalBytes, freeBytesAvailable;
      directory = Path.GetPathRoot(directory);
      if (!GetDiskFreeSpaceEx(directory, out freeBytesAvailable, out totalBytes, out freeBytes))
        Logger.Warn("GetDiskFreeSpaceEx failed (0x{0:x8})", Marshal.GetLastWin32Error());

      return new WebDiskSpaceInformation()
      {
        Disk = directory,
        Available = (float)Math.Round(freeBytes / 1024.0 / 1024 / 1024, 2),
        Size = (float)Math.Round(totalBytes / 1024.0 / 1024 / 1024, 2),
        Used = (float)Math.Round((totalBytes - freeBytes) / 1024.0 / 1024 / 1024, 2),
        PercentageUsed = totalBytes > 0 ? (float)(100 - Math.Round((float)freeBytes / (float)totalBytes * 100, 1)) : (float)0
      };
    }

    internal static ILogger Logger
    {
      get { return ServiceRegistration.Get<ILogger>(); }
    }
  }
}
