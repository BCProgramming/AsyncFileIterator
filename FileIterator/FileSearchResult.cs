using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace FileIterator
{
    public class FileSearchResult
    {
        [StructLayout(LayoutKind.Explicit)]
        struct LongToInt
        {
            [FieldOffset(0)]
            public long Long64;
            [FieldOffset(0)]
            public int LeftInt32;
            [FieldOffset(4)]
            public int RightInt32;
        }
        private NativeMethods.WIN32_FIND_DATA _FindData;
        private String _FullPath;
        public NativeMethods.WIN32_FIND_DATA FindData { get { return _FindData; } }
        public String FullPath { get { return _FullPath; } }
        private DateTime DateTimeFromFileTime(FILETIME source)
        {
            var str = new LongToInt() { LeftInt32 = source.dwLowDateTime, RightInt32 = source.dwHighDateTime };
            return DateTime.FromFileTime(str.Long64);
        }
        public DateTime DateCreated { get { return DateTimeFromFileTime(_FindData.ftCreationTime); } }
        public DateTime DateAccessed { get { return DateTimeFromFileTime(_FindData.ftLastAccessTime); } }
        public DateTime DateModified { get { return DateTimeFromFileTime(_FindData.ftLastWriteTime); } }
        public FileAttributes Attributes { get { return _FindData.dwFileAttributes; } }
        public String FileName { get { return _FindData.cFileName.Trim(); } }
        public FileSearchResult(NativeMethods.WIN32_FIND_DATA pFindData, String pFullPath)
        {
            
            _FindData = pFindData;
            _FullPath = pFullPath;
        }
    }
}
