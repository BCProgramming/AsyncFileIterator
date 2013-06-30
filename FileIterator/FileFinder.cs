using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;

namespace FileIterator
{
    public class FileFinder
    {
        /// <summary>
        ///     Method called with search results for processing.
        /// </summary>
        /// <param name="foundData">Data of found item.</param>
        public delegate void FoundRoutine(FileSearchResult foundData);

        /// <summary>
        ///     SearchFilter predicate function, used to determine if a found result should invoke the FoundRoutine.
        /// </summary>
        /// <param name="foundData">Data result of the file that was found.</param>
        /// <returns>true to indicate that this item passes the filter; false to reject it. </returns>
        public delegate bool SearchFilter(FileSearchResult foundData);

        private static readonly int MaxSpunThreads = 5;
        private readonly List<FileFinder> ChildSearchers = new List<FileFinder>();
        private readonly SearchFilter Filter;
        private readonly SearchFilter RecurseTest;
        //FileFinder class implements a queue-based algorithm that 
        //can call back asynchronously with each element as it is retrieved.
        private readonly String SearchMask;
        private readonly String SearchString;
        private readonly ConcurrentQueue<FileSearchResult> WorkQueue = new ConcurrentQueue<FileSearchResult>();
        private readonly FoundRoutine performAction;
        private Action<FileFinder> SearchComplete;
        private bool _Finished;
        private bool _SearchComplete;


        private BackgroundWorker bw;

        public FileFinder(String sDir, String sMask, SearchFilter pFilter, FoundRoutine act, bool recurse)
            : this(sDir, sMask, pFilter, act, recurse ? ((data) => true) : (SearchFilter) null)
        {
        }

        public FileFinder(String sDir, String sMask, SearchFilter pFilter, FoundRoutine act, SearchFilter RecursionTest)
        {
            if (sDir == null) throw new ArgumentNullException("sDir");
            if (sMask == null) throw new ArgumentNullException("sMask");
            if (act == null) throw new ArgumentNullException("act");
            SearchMask = sMask;
            String fullpath = Path.Combine(sDir, sMask);
            RecurseTest = RecursionTest;
            SearchString = fullpath;
            Filter = pFilter;
            performAction = act;
        }

        public bool Finished
        {
            get { return _Finished; }
        }

        public static IEnumerable<FileSearchResult> Enumerate(String sDir, String sMask, SearchFilter pFilter,
                                                                bool recursive)
        {
            return Enumerate(sDir, sMask, pFilter, recursive ? ((a) => true) : (SearchFilter) null);
        }

        public static IEnumerable<FileSearchResult> Enumerate(String sDir, String sMask, SearchFilter pFilter,
                                                                SearchFilter RecursionTest)
        {
            var buffer = new ConcurrentQueue<FileSearchResult>();
            var ff = new FileFinder(sDir, sMask, pFilter, buffer.Enqueue, RecursionTest);
            //start a thread, call ff.Start() from within the thread to prevent us from being blocked.
            var WorkThread = new Thread(ff.Start);
            WorkThread.Start();
            bool dequeueSuccess = false; //variables used to store if we dequeued an items successfully.
            do
            {
                FileSearchResult currentitem = null;

                if (dequeueSuccess = buffer.TryDequeue(out currentitem))
                {
                    yield return currentitem;
                }
            } while (dequeueSuccess || !ff.Finished);
            //loop while we are getting items or the Searcher is still working.
        }


        public void Cancel()
        {
            foreach (FileFinder iterate in ChildSearchers)
            {
                iterate.Cancel();
            }
            if (bw != null) bw.CancelAsync();
            _Finished = _SearchComplete = true;
        }

        public void Start()
        {
            _SearchComplete = false;
            _Finished = false;
            String usepath = SearchString.Substring(0, SearchString.Length - Path.GetFileName(SearchString).Length);
            if (bw == null)
                bw = new BackgroundWorker();
            //connect up the event handler for the background worker.
            bw.DoWork += bw_DoWork;
            bw.RunWorkerAsync();

            //if we are running a recursive search, create another Finder to search for directories.
            if (RecurseTest != null)
            {
                var DirFinder = new FileFinder(usepath, "*", (dat) =>
                                                                    {
                                                                        return (dat.Attributes &
                                                                                FileAttributes.Directory) ==
                                                                            FileAttributes.Directory &&
                                                                            dat.FileName != "." &&
                                                                            dat.FileName != ".." &&
                                                                            RecurseTest(dat);
                                                                        //find directories, but we don't want . or .. recursing into those folders is asking for trouble.
                                                                    }, (data) =>
                                                                        {
                                                                            //with each directory, create a new FileFinder that searches that directory
                                                                            //in the same way that we are.
                                                                            //we will add it to our ChildSearchers list and then get it to remove itself when it is finished.
                                                                            //this requires locking around the List<T>.
                                                                            var TargetSearcher =
                                                                                new FileFinder(data.FullPath,
                                                                                                SearchMask, Filter,
                                                                                                performAction,
                                                                                                RecurseTest);
                                                                            lock (ChildSearchers) { ChildSearchers.Add(TargetSearcher); };
                                                                            TargetSearcher.SearchComplete =
                                                                                (a) => {
                                                                                    lock(ChildSearchers){ChildSearchers.Remove(a);}
                                                                                };
                                                                            TargetSearcher.Start();
                                                                        }, false);
                DirFinder.Start();
            }

            //find all entries and add them to the work queue.


            NativeMethods.WIN32_FIND_DATA retrieved;
            IntPtr findHandle = NativeMethods.FindFirstFile(SearchString, out retrieved);

            //search as long as the findhandle is not a null pointer and is not the error value indicating we found all the files.
            while (findHandle != IntPtr.Zero && findHandle != NativeMethods.ERROR_NO_MORE_FILES)
            {
                String fullpath = Path.Combine(usepath, retrieved.cFileName);
                if (Filter == null || Filter(new FileSearchResult(retrieved, fullpath)))
                    WorkQueue.Enqueue(new FileSearchResult(retrieved, fullpath));

                //find next!
                if (!NativeMethods.FindNextFile(findHandle, out retrieved)) break; //if FindNextFile returns 0, break.
            }

            if (findHandle != IntPtr.Zero && findHandle != NativeMethods.ERROR_NO_MORE_FILES)
            {
                NativeMethods.FindClose(findHandle);
            }
            _SearchComplete = true;
        }

        private void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            var SpanThreads = new HashSet<Thread>(); //keep track of the Threads we've spun so far.
            int spuncount = 0; //number of spun threads.

            do
            {
                if (!WorkQueue.IsEmpty && spuncount < MaxSpunThreads)
                {
                    //if it's not empty, and we haven't hit the spun thread limit, we need to spin one out.
                    
                    FileSearchResult popped = null;
                    if (!bw.CancellationPending && WorkQueue.TryDequeue(out popped))
                    {
                        //if the dequeue was successful, spin off a new thread to perform action.
                        if (performAction != null)
                        {
                            var SpinIt = new Thread(() =>
                                                        {
                                                            performAction(popped);
                                                            lock (SpanThreads)
                                                            {
                                                                SpanThreads.Remove(Thread.CurrentThread);
                                                            }
                                                            ;
                                                        });
                            lock (SpanThreads)
                            {
                                SpanThreads.Add(SpinIt);
                            }
                            ;
                            SpinIt.Start();
                        }
                    }
                }


                //keep going as long as the search is not complete, the workqueue isn't empty, or there
                //are threads still running.
                lock (SpanThreads)
                {
                    spuncount = SpanThreads.Count;
                }
            } while (!_SearchComplete || !WorkQueue.IsEmpty || spuncount > 0);
            _Finished = true;
            if (SearchComplete != null) SearchComplete(this);
        }
    }
}