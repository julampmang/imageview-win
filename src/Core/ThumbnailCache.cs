using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ImageView.Core
{
    /// <summary>
    /// 缩略图缓存：单个低优先级后台线程解码，LRU 上限 120 张（约 6MB），
    /// 目录切换时整体作废。解码完成的缩略图通过同步上下文回到 UI 线程。
    /// </summary>
    internal sealed class ThumbnailCache : IDisposable
    {
        internal const int Cell = 128;
        private const int MaxEntries = 120;

        private readonly Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> _lru = new LinkedList<string>();
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _queue = new Queue<string>();
        private readonly object _gate = new object();
        private readonly SynchronizationContext _context;

        private Thread _worker;
        private volatile bool _stop;
        private int _generation;

        internal ThumbnailCache(SynchronizationContext context)
        {
            _context = context;
        }

        internal event Action<string> ThumbnailReady;

        /// <summary>取已缓存的缩略图；未命中返回 null。</summary>
        internal Bitmap TryGet(string path)
        {
            lock (_gate)
            {
                Bitmap bitmap;
                if (_cache.TryGetValue(path, out bitmap))
                {
                    LinkedListNode<string> node = _lru.Find(path);
                    if (node != null)
                    {
                        _lru.Remove(node);
                        _lru.AddFirst(node);
                    }
                    return bitmap;
                }
                return null;
            }
        }

        /// <summary>请求后台解码（重复请求会被忽略）。</summary>
        internal void Request(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            lock (_gate)
            {
                if (_cache.ContainsKey(path) || _pending.Contains(path))
                {
                    return;
                }
                _pending.Add(path);
                _queue.Enqueue(path);
            }
            EnsureWorker();
        }

        private void EnsureWorker()
        {
            if (_worker != null)
            {
                return;
            }
            _stop = false;
            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Priority = ThreadPriority.BelowNormal;
            _worker.Name = "ImageView.Thumbs";
            _worker.Start();
        }

        private void WorkerLoop()
        {
            while (!_stop)
            {
                string path = null;
                int generation;
                lock (_gate)
                {
                    generation = _generation;
                    if (_queue.Count > 0)
                    {
                        path = _queue.Dequeue();
                    }
                    else
                    {
                        _pending.Clear();
                    }
                }

                if (path == null)
                {
                    Thread.Sleep(60);
                    continue;
                }

                Bitmap thumb = null;
                try
                {
                    thumb = DecodeThumbnail(path);
                }
                catch (Exception ex)
                {
                    AppLog.Error("ThumbnailCache.Decode " + path, ex);
                }

                lock (_gate)
                {
                    _pending.Remove(path);
                    if (generation != _generation)
                    {
                        if (thumb != null)
                        {
                            thumb.Dispose();
                        }
                        continue;
                    }
                    if (thumb == null)
                    {
                        continue;
                    }
                    if (_cache.ContainsKey(path))
                    {
                        thumb.Dispose();
                        continue;
                    }
                    _cache[path] = thumb;
                    _lru.AddFirst(path);
                    TrimLocked();
                }

                Action<string> handler = ThumbnailReady;
                if (handler != null && _context != null)
                {
                    _context.Post(delegate { handler(path); }, null);
                }
            }
        }

        private static Bitmap DecodeThumbnail(string path)
        {
            int sw;
            int sh;
            bool alpha;
            Bitmap bitmap = null;
            if (WicDecoder.Available)
            {
                bitmap = WicDecoder.Decode(path, Cell, out sw, out sh, out alpha);
            }
            if (bitmap == null)
            {
                bitmap = GdiDecoder.Decode(path, Cell, out sw, out sh, out alpha);
            }
            return bitmap;
        }

        private void TrimLocked()
        {
            while (_cache.Count > MaxEntries && _lru.Count > 0)
            {
                LinkedListNode<string> last = _lru.Last;
                if (last == null)
                {
                    break;
                }
                _lru.RemoveLast();
                Bitmap bitmap;
                if (_cache.TryGetValue(last.Value, out bitmap))
                {
                    _cache.Remove(last.Value);
                    bitmap.Dispose();
                }
            }
        }

        /// <summary>换目录：作废全部缓存并让正在跑的解码结果被丢弃。</summary>
        internal void Reset()
        {
            lock (_gate)
            {
                _generation++;
                _queue.Clear();
                _pending.Clear();
                foreach (Bitmap bitmap in _cache.Values)
                {
                    bitmap.Dispose();
                }
                _cache.Clear();
                _lru.Clear();
            }
        }

        public void Dispose()
        {
            _stop = true;
            Reset();
        }
    }
}
