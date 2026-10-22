using System.Diagnostics;

namespace ImageView
{
    /// <summary>进程内的单调时钟（.NET Framework 没有 Environment.TickCount64）。</summary>
    internal static class Clock
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();

        /// <summary>自进程启动以来的毫秒数。</summary>
        internal static long NowMs
        {
            get { return Watch.ElapsedMilliseconds; }
        }
    }
}
