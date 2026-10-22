using System;
using System.Drawing;
using System.IO;

namespace ImageView.Core
{
    /// <summary>
    /// 把编辑参数落到磁盘：先按需要的边长取一档解码，再裁剪 + 套颜色矩阵，最后交给 GDI+ 编码。
    ///
    /// 内存策略和看图一致——只在用户按下"另存"时才解码，导出完立刻释放；
    /// 长边封顶见 <see cref="MaxExportSide"/>，避免为了导出把几百 MB 的位图一次性摊开。
    /// </summary>
    internal static class EditExporter
    {
        /// <summary>导出长边上限（约 4096² 的 32 位位图 ≈ 67 MB，和看图时的档位预算一致）。</summary>
        internal const int MaxExportSide = 4096;

        internal sealed class Result
        {
            internal bool Ok;
            internal string Error;
            internal string Path;
            internal int Width;
            internal int Height;
            internal bool Capped;
        }

        internal static string SuggestPath(string sourcePath, string extension)
        {
            string dir = System.IO.Path.GetDirectoryName(sourcePath) ?? ".";
            string name = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
            string candidate = System.IO.Path.Combine(dir, name + "-编辑" + extension);
            int index = 2;
            while (File.Exists(candidate))
            {
                candidate = System.IO.Path.Combine(dir, name + "-编辑" + index + extension);
                index++;
            }
            return candidate;
        }

        internal static Result Execute(ImageDocument document, EditRecipe recipe, string targetPath,
            GdiEncoder.Format format, int quality)
        {
            Result result = new Result();
            result.Path = targetPath;
            Bitmap output = null;
            Bitmap prepared = null;
            Bitmap rotated = null;
            try
            {
                if (document == null || !document.IsValid)
                {
                    result.Error = "当前没有可用的图片";
                    return result;
                }
                if (format == null)
                {
                    result.Error = "未选择输出格式";
                    return result;
                }

                int fullWidth = document.PixelWidth;
                int fullHeight = document.PixelHeight;
                Rectangle crop = recipe != null
                    ? recipe.CropPixels(fullWidth, fullHeight)
                    : new Rectangle(0, 0, fullWidth, fullHeight);

                int wantedSide = Math.Max(crop.Width, crop.Height);
                if (wantedSide > MaxExportSide)
                {
                    result.Capped = true;
                    wantedSide = MaxExportSide;
                }

                Bitmap source = document.GetTier(wantedSide);
                if (source == null)
                {
                    source = document.Preview;
                }
                if (source == null)
                {
                    result.Error = "读取源图失败";
                    return result;
                }

                // 拉直：先旋转并裁掉黑角，后续的裁剪与颜色都作用在旋转结果上
                if (recipe != null && recipe.HasRotation)
                {
                    rotated = recipe.Rotate(source);
                    if (rotated != null)
                    {
                        source = rotated;
                    }
                }

                // 归一化裁剪框映射到实际拿到的这一档
                Rectangle area = new Rectangle(0, 0, source.Width, source.Height);
                if (recipe != null)
                {
                    area = recipe.CropPixels(source.Width, source.Height);
                }

                if (recipe == null || !recipe.HasColor)
                {
                    if (area.Width == source.Width && area.Height == source.Height)
                    {
                        output = source.Clone() as Bitmap;
                    }
                    else
                    {
                        prepared = new Bitmap(area.Width, area.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                        using (Graphics g = Graphics.FromImage(prepared))
                        {
                            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                            g.DrawImage(source, new Rectangle(0, 0, area.Width, area.Height), area.X, area.Y,
                                area.Width, area.Height, GraphicsUnit.Pixel);
                        }
                        output = prepared;
                        prepared = null;
                    }
                }
                else
                {
                    output = recipe.Apply(source, area);
                }

                if (output == null)
                {
                    result.Error = "生成输出位图失败";
                    return result;
                }

                // 锐化放在最后：先保证颜色/尺寸正确，再对结果做 USM
                if (recipe != null && recipe.HasSharpen)
                {
                    Bitmap sharpened = UnsharpMask.Apply(output, recipe.Sharpen / 100f * 1.5f);
                    if (!ReferenceEquals(sharpened, output))
                    {
                        output.Dispose();
                        output = sharpened;
                    }
                }

                string temp = targetPath + ".tmp" + Guid.NewGuid().ToString("N").Substring(0, 6);
                string error;
                if (!GdiEncoder.TryEncode(output, temp, format, quality, out error))
                {
                    result.Error = error;
                    TryDelete(temp);
                    return result;
                }

                if (File.Exists(targetPath))
                {
                    File.Replace(temp, targetPath, null);
                }
                else
                {
                    File.Move(temp, targetPath);
                }

                result.Width = output.Width;
                result.Height = output.Height;
                result.Ok = true;
                return result;
            }
            catch (Exception ex)
            {
                AppLog.Error("EditExporter.Execute " + targetPath, ex);
                result.Error = ex.Message;
                return result;
            }
            finally
            {
                if (rotated != null && !ReferenceEquals(rotated, output))
                {
                    rotated.Dispose();
                }
                if (output != null)
                {
                    output.Dispose();
                }
                if (prepared != null)
                {
                    prepared.Dispose();
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
