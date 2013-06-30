using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices;

using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace FileIterator
{
   

    public static class DirectoryInfoExtensions
    {
        public static IEnumerable<FileInfo> IterateFiles(this DirectoryInfo di)
        {
            return di.IterateFiles(SearchOption.TopDirectoryOnly);
        }

        public static IEnumerable<FileInfo> IterateFiles(this DirectoryInfo di, SearchOption so)
        {
            return di.IterateFiles((fn, ff) => true,
                                   (dn, df) => so == SearchOption.AllDirectories && FSEnumerator.RecursionTest(dn, df));
        }

        public static IEnumerable<FileInfo> IterateFiles(this DirectoryInfo di, FSEnumerator.EntryTestRoutine fileTest,
                                                         SearchOption so)
        {
            return di.IterateFiles(fileTest,
                                   (dn, df) => so == SearchOption.AllDirectories && FSEnumerator.RecursionTest(dn, df));
        }

        public static IEnumerable<FileInfo> IterateFiles(this DirectoryInfo di, FSEnumerator.EntryTestRoutine fileTest,
                                                         FSEnumerator.EntryTestRoutine recurseTest)
        {
            return
                FSEnumerator.FindFiles(di.FullName, fileTest, recurseTest)
                            .Select(iterate => new FileInfo(iterate.FullFileName));
        }
    }
    public class FSEnumerator
    {
        /// <summary>
        /// delegate method used for testing if a Directory should be recursed into.
        /// </summary>
        /// <param name="Name">Name of the directory. Does not include full path.</param>
        /// <param name="FullPath">Full path to this directory.</param>
        /// <returns>true to indicate that this Filter passes. False otherwise.</returns>
        public delegate bool EntryTestRoutine(String Name, String FullPath);

        //default EntryTestRoutine implementation for Directory Recursion.
        private static readonly String[] ExcludeFolders = new String[] {".", ".."};

        private static bool FitsMask(string sFileName, string sFileMask)
        {
            Regex mask = new Regex(sFileMask.Replace(".", "[.]").Replace("*", ".*").Replace("?", "."));
            return mask.IsMatch(sFileName);
        }

        internal static bool RecursionTest(String dName, String FullPath)
        {
            return !ExcludeFolders.Contains(dName);
        }

        public static IEnumerable<NativeMethods.FileFindData> FindFiles(String sDirectory, String sMask, bool recurse)
        {
            return FindFiles(sDirectory, RecursionTest, (fn, ff) => FitsMask(fn, sMask));
        }

        public static IEnumerable<NativeMethods.FileFindData> FindFiles(String sDirectory, EntryTestRoutine recurseTest,
                                                                EntryTestRoutine filterTest)
        {
            recurseTest = recurseTest ?? RecursionTest;
            filterTest = filterTest ?? ((ds, fp) => FitsMask(ds, "*"));


            String usemask = Path.Combine(sDirectory, "*");

            foreach (var iterate in FindFiles(usemask))
            {
                if (((FileAttributes) iterate.dwFileAttributes).HasFlag(FileAttributes.Directory))
                {
                    String subdir = Path.Combine(sDirectory, iterate.Filename);
                    if (recurseTest(iterate.Filename, subdir))
                    {
                        //recursive call.

                        foreach (var subiterate in FindFiles(subdir, recurseTest, filterTest))
                        {
                            var copied = subiterate;
                            copied.FullFileName = Path.Combine(subdir, subiterate.FullFileName);
                            yield return copied;
                        }
                    }
                }
                else
                {
                    String fullpath = Path.Combine(sDirectory, iterate.Filename);
                    if (filterTest(iterate.Filename, fullpath))
                    {
                        NativeMethods.FileFindData ffd = new NativeMethods.FileFindData();
                        ffd.APIStructure = iterate;
                        ffd.FullFileName = fullpath;
                        yield return ffd;
                    }
                }
            }
        }

        public static IAsyncEnumerable<NativeMethods.WIN32_FIND_DATA> FindFilesAsync(String sMask)
        {
            return FindFiles(sMask).ToAsyncEnumerable();
        }

        public static IEnumerable<NativeMethods.FileFindData> FindFilesAsync(String sDirectory, String sMask, bool Recursive)
        {
            return FindFilesAsync(Path.Combine(sDirectory, sMask), (a, s) => !(s == "." || s == ".."),
                                  (a, sf) => FitsMask(sf, sMask));
        }

        public static IEnumerable<NativeMethods.FileFindData> FindFilesAsync(String sDirectory, EntryTestRoutine recurseTest,
                                                                     EntryTestRoutine filterTest)
        {
            recurseTest = recurseTest ?? RecursionTest;
            filterTest = filterTest ?? ((ds, fp) => FitsMask(ds, "*"));


            String usemask = Path.Combine(sDirectory, "*");
            var gotresult = FindFilesAsync(usemask);
            var enumerator = gotresult.GetEnumerator();
            Task<bool> asyncresult;
            (asyncresult = enumerator.MoveNext()).Wait();

            var iterate = enumerator.Current;

            bool docontinue = false;
            do
            {
                iterate = enumerator.Current;
                var AdvanceResult = enumerator.MoveNext();
                //AdvanceResult.Start();
                if (((FileAttributes) iterate.dwFileAttributes).HasFlag(FileAttributes.Directory))
                {
                    String subdir = Path.Combine(sDirectory, iterate.Filename);
                    if (recurseTest(iterate.Filename, subdir))
                    {
                        //recursive call.

                        foreach (var subiterate in FindFilesAsync(subdir, recurseTest, filterTest))
                        {
                            var copied = subiterate;
                            copied.FullFileName = Path.Combine(subdir, subiterate.FullFileName);
                            yield return copied;
                        }
                    }
                }
                else
                {
                    String fullpath = Path.Combine(sDirectory, iterate.Filename);
                    if (filterTest(iterate.Filename, fullpath))
                    {
                        NativeMethods.FileFindData ffd = new NativeMethods.FileFindData();
                        ffd.APIStructure = iterate;
                        ffd.FullFileName = fullpath;
                        yield return ffd;
                    }
                }

                AdvanceResult.Wait();
                docontinue = AdvanceResult.Result;
            } while (docontinue);
        }


        public static IEnumerable<NativeMethods.WIN32_FIND_DATA> FindFiles(String sMask)
        {
            String usemask = sMask;
            NativeMethods.WIN32_FIND_DATA fdata;
            IntPtr fHandle = IntPtr.Zero;
            try
            {
                fHandle = NativeMethods.FindFirstFile(usemask, out fdata);
                while (fHandle != IntPtr.Zero)
                {
                    yield return fdata;
                    if (!NativeMethods.FindNextFile(fHandle, out fdata)) break;
                }
            }
            finally
            {
                if (fHandle != IntPtr.Zero) NativeMethods.FindClose(fHandle);
            }
        }

        //FileSystem Enumerator
        public static IEnumerable<NativeMethods.WIN32_FIND_DATA> FindFiles(String sDirectory, String sMask)
        {
            NativeMethods.WIN32_FIND_DATA fdata;
            String usemask = Path.Combine(sDirectory, sMask);
            foreach (var iterate in FindFiles(usemask)) yield return iterate;
        }

        public static IEnumerable<NativeMethods.WIN32_FIND_DATA> FindFilesAsync(String sDirectory, String sMask)
        {
            NativeMethods.WIN32_FIND_DATA fdata;
            String usemask = Path.Combine(sDirectory, sMask);

            var iterateover = FindFilesAsync(usemask);
            var enumer = iterateover.GetEnumerator();
            bool docontinue = false;
            do
            {
                yield return enumer.Current;
                var waiter = enumer.MoveNext();
                docontinue = waiter.Result;
            } while (docontinue);
        }
    }
}