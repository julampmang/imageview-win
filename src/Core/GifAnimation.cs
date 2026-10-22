using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ImageView.Core
{
    /// <summary>
    /// GIF 动图播放：用 GDI+ 逐帧取出，按帧延时驱动计时器。
    ///
    /// 帧合成策略：把每一帧画到一张持久的画布上（SourceOver）。
    /// 这是 GIF disposal=none 的语义，也是绝大多数动图的实际行为；
    /// 少数使用「还原为背景」的动图可能有残影，属于已知取舍（换取实现简单、无额外解码）。
    /// </summary>
    internal sealed class GifAnimation : IDisposable
    {
        private readonly string _path;
        private Image _frames;
        private FrameDimension _dimension;
        private int _frameCount;
        private int[] _delays;
        private Bitmap _canvas;
        private int _current;
        private System.Windows.Forms.Timer _timer;
        private bool _running;
        private bool _disposed;

        internal GifAnimation(string path)
        {
            _path = path;
        }

        internal Bitmap Current
        {
            get { return _canvas; }
        }

        internal int FrameCount
        {
            get { return _frameCount; }
        }

        internal event EventHandler FrameChanged;

        internal bool Load()
        {
            try
            {
                _frames = Image.FromFile(_path);
                _dimension = new FrameDimension(_frames.FrameDimensionsList.Length > 0
                    ? _frames.FrameDimensionsList[0]
                    : FrameDimension.Time.Guid);
                _frameCount = _frames.GetFrameCount(_dimension);
                if (_frameCount <= 1)
                {
                    Dispose();
                    return false;
                }

                _delays = new int[_frameCount];
                for (int i = 0; i < _frameCount; i++)
                {
                    _delays[i] = 100;
                }
                try
                {
                    PropertyItem item = _frames.GetPropertyItem(0x5100); // FrameDelay
                    if (item != null && item.Value != null)
                    {
                        int count = item.Value.Length / 4;
                        for (int i = 0; i < _frameCount && i < count; i++)
                        {
                            int centiseconds = BitConverter.ToInt32(item.Value, i * 4);
                            if (centiseconds > 0)
                            {
                                _delays[i] = centiseconds * 10;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // 没有延时信息就用默认 100ms
                }

                _canvas = new Bitmap(_frames.Width, _frames.Height, PixelFormat.Format32bppPArgb);
                _current = 0;
                RenderFrame(0);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Error("GifAnimation.Load " + _path, ex);
                Dispose();
                return false;
            }
        }

        internal void Start()
        {
            if (_disposed || _frameCount <= 1)
            {
                return;
            }
            if (_timer == null)
            {
                _timer = new System.Windows.Forms.Timer();
                _timer.Tick += OnTick;
            }
            _running = true;
            _timer.Interval = Math.Max(20, _delays[_current]);
            _timer.Start();
        }

        internal void Stop()
        {
            _running = false;
            if (_timer != null)
            {
                _timer.Stop();
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_disposed || !_running)
            {
                return;
            }
            _current = (_current + 1) % _frameCount;
            RenderFrame(_current);
            if (_timer != null)
            {
                _timer.Interval = Math.Max(20, _delays[_current]);
            }
            EventHandler handler = FrameChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RenderFrame(int index)
        {
            if (_frames == null || _canvas == null)
            {
                return;
            }
            try
            {
                _frames.SelectActiveFrame(_dimension, index);
                using (Graphics g = Graphics.FromImage(_canvas))
                {
                    g.CompositingMode = CompositingMode.SourceOver;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(_frames, new Rectangle(0, 0, _canvas.Width, _canvas.Height));
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("GifAnimation.RenderFrame", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Stop();
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
            if (_canvas != null)
            {
                _canvas.Dispose();
                _canvas = null;
            }
            if (_frames != null)
            {
                _frames.Dispose();
                _frames = null;
            }
        }
    }
}
