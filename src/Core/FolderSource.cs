using System;
using System.Collections.Generic;
using System.IO;

namespace ImageView.Core
{
    /// <summary>目录里的图片列表：自然排序、上一张/下一张、索引查找。</summary>
    internal sealed class FolderSource
    {
        private readonly List<string> _files = new List<string>();
        private string _directory;

        internal string Directory
        {
            get { return _directory; }
        }

        internal int Count
        {
            get { return _files.Count; }
        }

        internal string PathAt(int index)
        {
            if (index < 0 || index >= _files.Count)
            {
                return null;
            }
            return _files[index];
        }

        internal int IndexOf(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return -1;
            }
            for (int i = 0; i < _files.Count; i++)
            {
                if (string.Equals(_files[i], path, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>换目录才重新枚举；返回是否真的换了。</summary>
        internal bool LoadFor(string filePath)
        {
            string directory;
            try
            {
                directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            }
            catch (Exception)
            {
                return false;
            }
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }
            if (_directory != null && string.Equals(_directory, directory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _directory = directory;
            _files.Clear();
            Enumerate(directory);
            return true;
        }

        private void Enumerate(string directory)
        {
            try
            {
                string[] entries = System.IO.Directory.GetFiles(directory);
                List<string> images = new List<string>(entries.Length);
                for (int i = 0; i < entries.Length; i++)
                {
                    if (Formats.IsImageFile(entries[i]))
                    {
                        images.Add(entries[i]);
                    }
                }
                images.Sort(NaturalComparer.Instance);
                _files.AddRange(images);
            }
            catch (Exception ex)
            {
                AppLog.Error("FolderSource.Enumerate " + directory, ex);
            }
        }

        /// <summary>文件名自然排序：img2 在 img10 前面，大小写不敏感。</summary>
        private sealed class NaturalComparer : IComparer<string>
        {
            internal static readonly NaturalComparer Instance = new NaturalComparer();

            public int Compare(string x, string y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }
                if (x == null)
                {
                    return -1;
                }
                if (y == null)
                {
                    return 1;
                }

                int i = 0;
                int j = 0;
                while (i < x.Length && j < y.Length)
                {
                    char cx = x[i];
                    char cy = y[j];
                    bool dx = char.IsDigit(cx);
                    bool dy = char.IsDigit(cy);

                    if (dx && dy)
                    {
                        int startI = i;
                        int startJ = j;
                        while (i < x.Length && char.IsDigit(x[i])) { i++; }
                        while (j < y.Length && char.IsDigit(y[j])) { j++; }
                        string nx = x.Substring(startI, i - startI).TrimStart('0');
                        string ny = y.Substring(startJ, j - startJ).TrimStart('0');
                        if (nx.Length != ny.Length)
                        {
                            return nx.Length - ny.Length;
                        }
                        int cmpNum = string.CompareOrdinal(nx, ny);
                        if (cmpNum != 0)
                        {
                            return cmpNum;
                        }
                        continue;
                    }

                    char lx = char.ToLowerInvariant(cx);
                    char ly = char.ToLowerInvariant(cy);
                    if (lx != ly)
                    {
                        return lx.CompareTo(ly);
                    }
                    i++;
                    j++;
                }
                return (x.Length - i) - (y.Length - j);
            }
        }
    }
}
