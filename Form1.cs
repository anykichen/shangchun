using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DragUploadToNas
{
    public class MainForm : Form
    {
        private static readonly string NasWebDavUrl = "http://10.201.2.31:5005/tpk%20%E5%85%B1%E4%BA%AB%E7%BB%99%E6%88%91/";
        private static readonly string UserName = "user";
        private static readonly string Pwd = "Cc880821/";

        private NotifyIcon _tray;
        private bool _isUploading;
        private bool _isDragOver;

        public MainForm()
        {
            InitUI();
            InitTray();
        }

        private void InitUI()
        {
            this.Text = "上传到 NAS";
            this.Size = new Size(160, 160);
            this.MinimumSize = this.MaximumSize = this.Size;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;
            this.TopMost = true;
            this.AllowDrop = true;
            this.DoubleBuffered = true;

            this.Paint += OnPaint;
            this.DragEnter += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                    _isDragOver = true;
                    Invalidate();
                }
            };
            this.DragLeave += (s, e) => { _isDragOver = false; Invalidate(); };
            this.DragDrop += OnDrop;

            var ctx = new ContextMenuStrip();
            ctx.Items.Add("退出", null, (s, e) => Application.Exit());
            this.ContextMenuStrip = ctx;

            this.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left)
                {
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(this.Handle, 0xA1, 2, 0);
                }
            };
        }

        private void OnPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = this.Width, h = this.Height;
            bool hover = _isDragOver;
            bool busy  = _isUploading;

            Color bodyColor = busy  ? Color.FromArgb(255, 180, 60)
                            : hover ? Color.FromArgb(255, 210, 80)
                            :         Color.FromArgb(255, 196, 57);
            Color tabColor  = busy  ? Color.FromArgb(230, 150, 30)
                            : hover ? Color.FromArgb(240, 185, 50)
                            :         Color.FromArgb(230, 168, 30);

            // 阴影
            using (var sb = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                g.FillEllipse(sb, 10, h - 22, w - 20, 16);

            // 标签
            var tab = new[] {
                new PointF(18, 30), new PointF(62, 30),
                new PointF(72, 42), new PointF(18, 42)
            };
            using (var sb = new SolidBrush(tabColor))
                g.FillPolygon(sb, tab);

            // 主体
            var body = RoundedRect(new Rectangle(10, 40, w - 20, h - 60), 10);
            using (var sb = new SolidBrush(bodyColor))
                g.FillPath(sb, body);

            // 高光
            using (var lg = new LinearGradientBrush(
                new PointF(10, 40), new PointF(10, 40 + (h - 60) * 0.5f),
                Color.FromArgb(80, 255, 255, 255), Color.Transparent))
            {
                var top = RoundedRect(new Rectangle(10, 40, w - 20, (h - 60) / 2), 10);
                g.FillPath(lg, top);
            }

            // 状态文字/图标
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            var rect = new RectangleF(10, 48, w - 20, h - 65);

            if (busy)
            {
                using (var f = new Font("Segoe UI", 9, FontStyle.Bold))
                using (var sb = new SolidBrush(Color.FromArgb(160, 80, 0)))
                    g.DrawString("上传中...", f, sb, rect, sf);
            }
            else if (hover)
            {
                using (var f = new Font("Segoe UI", 24))
                using (var sb = new SolidBrush(Color.FromArgb(160, 80, 40, 0)))
                    g.DrawString("↓", f, sb, rect, sf);
            }
            else
            {
                using (var p = new Pen(Color.FromArgb(80, 160, 100, 0), 2))
                {
                    int cx = w / 2, cy = h / 2 + 5;
                    g.DrawLine(p, cx - 20, cy - 8, cx + 20, cy - 8);
                    g.DrawLine(p, cx - 20, cy,     cx + 20, cy);
                    g.DrawLine(p, cx - 20, cy + 8, cx + 14, cy + 8);
                }
            }
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            _isDragOver = false;
            Invalidate();

            if (_isUploading) { Notify("正在上传中，请稍候..."); return; }

            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            var files = paths.SelectMany(p =>
                File.Exists(p)      ? new[] { p } :
                Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories) :
                Array.Empty<string>()
            ).ToList();

            if (files.Count == 0) return;

            Notify($"开始上传 {files.Count} 个文件...");
            _isUploading = true;
            Invalidate();

            Task.Run(async () =>
            {
                int ok = 0, skip = 0, fail = 0;

                for (int i = 0; i < files.Count; i++)
                {
                    var localPath = files[i];
                    var fileName  = Path.GetFileName(localPath);
                    var remoteUrl = NasWebDavUrl + Uri.EscapeDataString(fileName);
                    var fileInfo  = new FileInfo(localPath);

                    if (i == 0 || (i + 1) % 10 == 0 || i == files.Count - 1)
                        Notify($"[{i + 1}/{files.Count}] {fileName}");

                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        try
                        {
                            if (await RemoteFileExistsAsync(remoteUrl))
                            { skip++; break; }

                            await UploadFileAsync(localPath, remoteUrl, fileInfo.Length);
                            ok++; break;
                        }
                        catch (Exception ex)
                        {
                            string err = ex is WebException we && we.Response is HttpWebResponse wr
                                ? $"HTTP {(int)wr.StatusCode}"
                                : ex.Message;

                            if (attempt == 3)
                            {
                                fail++;
                                Notify($"失败: {fileName}\n{err}", ToolTipIcon.Error);
                            }
                            else await Task.Delay(1500);
                        }
                    }
                }

                this.Invoke(new Action(() => {
                    _isUploading = false;
                    Invalidate();
                }));

                Notify($"完成  ✓{ok}  跳过{skip}  ✗{fail}",
                    fail > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
            });
        }

        private async Task<bool> RemoteFileExistsAsync(string url)
        {
            try
            {
                var req = WebRequest.CreateHttp(url);
                req.Method = "HEAD";
                req.Credentials = new NetworkCredential(UserName, Pwd);
                req.Timeout = 8000;
                using (await req.GetResponseAsync()) return true;
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse r && r.StatusCode == HttpStatusCode.NotFound)
            { return false; }
            catch { return false; }
        }

        private async Task UploadFileAsync(string localPath, string remoteUrl, long fileSize)
        {
            const int BufSize = 256 * 1024;
            var buf = new byte[BufSize];

            var req = WebRequest.CreateHttp(remoteUrl);
            req.Method = "PUT";
            req.Credentials = new NetworkCredential(UserName, Pwd);
            req.ContentLength = fileSize;
            req.Timeout = 120_000;
            req.ReadWriteTimeout = 300_000;
            req.PreAuthenticate = true;

            using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read))
            using (var rs = await Task.Run(() => req.GetRequestStream()))
            {
                int read;
                while ((read = await fs.ReadAsync(buf, 0, BufSize)) > 0)
                    await rs.WriteAsync(buf, 0, read);
            }

            using (var resp = (HttpWebResponse)await Task.Run(() => req.GetResponse()))
            {
                var sc = resp.StatusCode;
                if (sc != HttpStatusCode.Created && sc != HttpStatusCode.NoContent && sc != HttpStatusCode.OK)
                    throw new Exception($"服务器返回 {(int)sc}");
            }
        }

        private void InitTray()
        {
            _tray = new NotifyIcon
            {
                Icon    = SystemIcons.Application,
                Visible = true,
                Text    = "NAS 上传"
            };
            var ctx = new ContextMenuStrip();
            ctx.Items.Add("退出", null, (s, e) => Application.Exit());
            _tray.ContextMenuStrip = ctx;
            _tray.DoubleClick += (s, e) => { this.Show(); this.BringToFront(); };

            this.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                { e.Cancel = true; this.Hide(); }
            };
        }

        private void Notify(string msg, ToolTipIcon icon = ToolTipIcon.Info)
        {
            try { _tray?.ShowBalloonTip(2500, "NAS 上传", msg, icon); }
            catch { }
        }

        protected override void Dispose(bool d)
        {
            if (d) _tray?.Dispose();
            base.Dispose(d);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    }
}
