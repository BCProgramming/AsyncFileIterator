using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileIterator
{
    class SearchResultEventArgs:EventArgs
    {
        private FileSearchResult _Result;
        public FileSearchResult Result { get { return _Result; } }
        public SearchResultEventArgs(FileSearchResult pResult)
        {
            _Result = pResult;
        }
    }
}
