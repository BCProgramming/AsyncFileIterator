using System;
using System.IO;
using System.Runtime.InteropServices;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace FileIterator
{
    public class NativeMethods
    {
        public const int MAX_PATH = 260;
        public const int MAX_ALTERNATE = 14;

        public static readonly IntPtr ERROR_NO_MORE_FILES = new IntPtr(18);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindFirstFile(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern bool FindNextFile(IntPtr hFindFile, out WIN32_FIND_DATA
                                                                     lpFindFileData);

        [DllImport("kernel32.dll")]
        public static extern bool FindClose(IntPtr hFindFile);

        public struct FileFindData
        {
            public WIN32_FIND_DATA APIStructure;
            public String FullFileName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATA
        {
            public FileAttributes dwFileAttributes;
            public FILETIME ftCreationTime;
            public FILETIME ftLastAccessTime;
            public FILETIME ftLastWriteTime;
            public uint nFileSizeHigh; //changed all to uint, otherwise you run into unexpected overflow
            public uint nFileSizeLow; //|
            public uint dwReserved0; //|
            public uint dwReserved1; //v
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_ALTERNATE)] public string cAlternate;

            public long FileSize
            {
                get
                {
                    long b = nFileSizeLow;
                    b = b << 32;
                    b = b | (uint) nFileSizeHigh;
                    return b;
                }
            }

            public String Filename
            {
                get { return cFileName.Replace("\0", "").Trim(); }
            }
        }
    }
}