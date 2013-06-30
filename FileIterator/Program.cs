using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileIterator
{
    class Program
    {
        static void FileFound(FileSearchResult founddata)
        {
            Console.WriteLine("Found:" + founddata.FullPath);
            Console.WriteLine("Created:" + founddata.DateCreated.ToString());

        }
        static void Main(string[] args)
        {
         
            foreach (var iterate in FileFinder.Enumerate("L:\\web", "*.png", null, (a) => true))
            {
                FileFound(iterate);
            }
          
            Console.ReadKey();
        }
    }
}
