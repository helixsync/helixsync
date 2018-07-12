using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Collections.ObjectModel;

namespace HelixSync.FileSystem
{
    ///<summary>
    ///Provides a wrapper for a system directory. Supports -WhatIf (changes are not saved to disk)
    ///<summary>
    public class FSDirectory : FSEntry, IFSDirectoryCore
    {
        public FSDirectory(string path, bool whatIf)
            : this(new DirectoryInfo(path), null, whatIf)
        {

        }

        internal FSDirectory(DirectoryInfo directoryInfo, FSDirectory parent, bool whatIf)
            : base(directoryInfo.FullName, parent, whatIf)
        {
            this.PopulateFromInfo(directoryInfo);
        }

        


        public bool IsLoaded { get; private set; }
        bool IsLoadedDeep { get; set; }
        public void Load(bool deep = false)
        {
            if (IsLoadedDeep)
                return;
            if (IsLoaded && !deep)
                return;

            Load(new DirectoryInfo(FullName), deep);
        }
        protected void Load(DirectoryInfo directoryInfo, bool deep)
        {
            if (IsLoadedDeep)
                return;
            if (IsLoaded)
            {
                if (deep)
                {
                    foreach (var child in children.OfType<FSDirectory>())
                        child.Load(true);
                }
                IsLoadedDeep = true;
                return;
            }

            PopulateFromInfo(directoryInfo);
            var IOChildren = (directoryInfo).EnumerateFileSystemInfos().ToArray();
            foreach (var entry in IOChildren)
            {
                if (entry is FileInfo childFileInfo)
                    children.Add(new FSFile(childFileInfo, this, WhatIf));
                else if (entry is DirectoryInfo childDirectoryInfo)
                {
                    var newChild = new FSDirectory(childDirectoryInfo, this, WhatIf);
                    children.Add(newChild);
                    if (deep)
                        newChild.Load(childDirectoryInfo, true);
                }
            }
            IsLoaded = true;
            if (deep)
                IsLoadedDeep = true;
        }

        private FSEntryCollection children = new FSEntryCollection(HelixUtil.FileSystemCaseSensitive);



        public IEnumerable<FSEntry> GetEntries(SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            if (searchOption == SearchOption.AllDirectories)
                Load(true);
            else
                Load(false);

            foreach (var entry in children)
            {
                yield return entry;
                if (searchOption == SearchOption.AllDirectories && entry is FSDirectory entryDirectory)
                {
                    foreach (var grandchildEntry in entryDirectory.GetEntries(SearchOption.AllDirectories))
                        yield return grandchildEntry;
                }
            }
        }

        /// <summary>
        /// Returns the FSEntry for the path. Returns null if not found.
        /// </summary>
        /// <param name="path">Path can be relative to the directory or absolute</param>
        public FSEntry TryGetEntry(string path)
        {

            if (path == null)
                throw new ArgumentNullException(nameof(path));

            path = HelixUtil.PathUniversal(path);

            if (Path.IsPathRooted(path))
                path = RemoveRootFromPath(path, FullName);

            if (path == "")
                return this;

            Load();

            var split = path.Split(HelixUtil.UniversalDirectorySeparatorChar);
            if (!children.Contains(split[0]))
                return null;
            else if (split.Length == 1)
                return children[split[0]];
            else
                return (children[split[0]] as FSDirectory)?
                    .TryGetEntry(string.Join(HelixUtil.UniversalDirectorySeparatorChar.ToString(), split.Skip(1).ToArray()));
        }

        public void RefreshEntry(string path)
        {
            path = HelixUtil.PathUniversal(path);

            var split = path.Split(HelixUtil.UniversalDirectorySeparatorChar);
            FSEntry newEntry;

            FileSystemInfo info;
            string fullPath = HelixUtil.JoinUniversal(this.FullName, split[0]);
            if (System.IO.Directory.Exists(fullPath))
            {
                info = new DirectoryInfo(fullPath);
            }
            else if (System.IO.File.Exists(fullPath))
            {
                info = new FileInfo(fullPath);
            }
            else
            {
                info = null;
            }

            if (info is DirectoryInfo dirInfo)
            {
                newEntry = new FSDirectory(dirInfo, this, this.WhatIf);
            }
            else if (info is FileInfo fileInfo)
            {
                newEntry = new FSFile(fileInfo, this, this.WhatIf);
            }
            else
            {
                newEntry = null; //not found
            }

            FSEntry oldEntry;
            children.TryGetValue(split[0], out oldEntry);
            if (newEntry?.EntryType != oldEntry?.EntryType)
            {
                if (oldEntry != null)
                    children.Remove(oldEntry);
                if (newEntry != null)
                    children.Add(newEntry);
            }
            else if (newEntry != null && oldEntry != null)
            {
                oldEntry.PopulateFromInfo(newEntry.LastWriteTimeUtc, newEntry.Length);
                newEntry = oldEntry;
            }

            if (newEntry != null)
            {
                children.Add(newEntry);
                if (split.Length > 1)
                    (newEntry as FSDirectory)?.RefreshEntry(HelixUtil.JoinUniversal(split.Skip(1).ToArray()));
            }
        }

        /// <summary>
        /// Returns if the file or directory exists.
        /// </summary>
        /// <param name="path">Path can be relitive to the directory or absolute</param>
        public bool Exists(string path)
        {
            return TryGetEntry(path) != null;
        }

        /// <summary>
        /// Removes the directory. If recursive is set to true also remove children, if false throws an exception if children exist
        /// </summary>
        /// <param name="recursive"></param>
        public void Delete(bool recursive = false)
        {
            if (!recursive && this.children.Any())
                throw new IOException("Directory is not empty");

            if (!WhatIf)
                Directory.Delete(FullName, recursive);
            ((IFSDirectoryCore)Parent).Remove(this);
        }



        void IFSDirectoryCore.Remove(FSEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            children.Remove(entry);
        }

        void IFSDirectoryCore.Add(FSEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (entry.Parent != this)
                throw new ArgumentException(nameof(entry), "Unable to add entry, must have parent set to self");

            children.Add(entry);
        }

        
    }




}