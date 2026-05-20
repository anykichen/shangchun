using System;
using System.Drawing;
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
        private static readonly string NasWebDavUrl = "http://10.201.2.31:5005/uploads/";
        private static readonly string UserName = "user";
        private static readonly string Pwd = "Cc880821/";

        private ListView lstFiles;
        private Label lblDropHint;
        private ProgressBar progressBar;
        private Label lblStatus;

        private int _successCount, _skipCount, _failCount;
        private CancellationTokenSource _cts;
        private bool _isUploading;

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "DragUploadToNas";
            this.Size = new Size(550, 450);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new Size(400, 300);
            this.AllowDrop = true;
            this.DragEnter += MainForm_DragEnter;
            this.DragOver += MainForm_DragOver;
            this.DragDrop += MainForm_DragDrop;
            this.BackColor = Color.FromArgb(32, 32, 32);

            lblDropHint = new Label
            {
                Text = "将文件或文件夹拖拽到此处\n\n松开即开始上传",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Segoe UI", 16)
            };
            this.Controls.Add(lblDropHint);

            lstFiles = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10),
                Visible = false
            };
            lstFiles.Columns.Add("文件名", 280);
            lstFiles.Columns.Add("大小", 70);
            lstFiles.Columns.Add("状态", 180);
            this.Controls.Add(lstFiles);

            var statusPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = Color.FromArgb(48, 48, 48)
            };
            this.Controls.Add(statusPanel);

            progressBar = new ProgressBar
            {
                Location = new Point(10, 8),
                Size = new Size(400, 20),
                Style = ProgressBarStyle.Continuous,
                ForeColor = Color.FromArgb(0, 120, 215)
            };
            statusPanel.Controls.Add(progressBar);

            lblStatus = new Label
            {
                Text = "就绪",
                Location = new Point(420, 10),
                AutoSize = true,
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 9)
            };
            statusPanel.Controls.Add(lblStatus);
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                lblDropHint.ForeColor = Color.FromArgb(180, 180, 180);
            }
        }

        private void MainForm_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Copy;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            lblDropHint.ForeColor = Color.FromArgb(100, 100, 100);
            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            StartUpload(paths);
        }

        private void StartUpload(string[] paths)
        {
            if (_isUploading) return;

            var files = paths.SelectMany(p =>
                File.Exists(p) ? new[] { p } :
                Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories) :
                Array.Empty<string>()
            ).ToList();

            if (files.Count == 0) return;

            lstFiles.Visible = true;
            lblDropHint.Visible = false;
            lstFiles.Items.Clear();
            _successCount = _skipCount = _failCount = 0;

            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                var item = new ListViewItem(Path.GetFileName(f));
                item.SubItems.Add(FormatSize(fi.Length));
                item.SubItems.Add("等待");
                lstFiles.Items.Add(item);
            }

            lblStatus.Text = $"共 {files.Count} 个文件，开始上传...";
            _isUploading = true;
            _cts = new CancellationTokenSource();
            Task.Run(() => DoUpload(files, _cts.Token));
        }

        private async Task DoUpload(System.Collections.Generic.List<string> files, CancellationToken ct)
        {
            int success = 0, skip = 0, fail = 0;

            for (int i = 0; i < files.Count; i++)
            {
                if (ct.IsCancellationRequested) break;

                var localPath = files[i];
                var fileName = Path.GetFileName(localPath);
                var fileInfo = new FileInfo(localPath);
                var remoteUrl = NasWebDavUrl + Uri.EscapeDataString(fileName);

                int capturedI = i;
                BeginInvoke(new Action(() =>
                {
                    lblStatus.Text = $"[{capturedI + 1}/{files.Count}] {fileName}";
                    progressBar.Value = (capturedI + 1) * 100 / files.Count;
                    if (capturedI < lstFiles.Items.Count)
                    {
                        lstFiles.Items[capturedI].SubItems[2].Text = "上传中";
                        lstFiles.Items[capturedI].SubItems[2].ForeColor = Color.Orange;
                    }
                }));

                bool done = false;

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        if (await RemoteFileExistsAsync(remoteUrl))
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (capturedI < lstFiles.Items.Count)
                                {
                                    lstFiles.Items[capturedI].SubItems[2].Text = "已存在(跳过)";
                                    lstFiles.Items[capturedI].SubItems[2].ForeColor = Color.Gray;
                                }
                            }));
                            skip++;
                            done = true;
                            break;
                        }

                        await UploadFileAsync(localPath, remoteUrl, fileInfo.Length, ct);

                        BeginInvoke(new Action(() =>
                        {
                            if (capturedI < lstFiles.Items.Count)
                            {
                                lstFiles.Items[capturedI].SubItems[2].Text = "完成";
                                lstFiles.Items[capturedI].SubItems[2].ForeColor = Color.Lime;
                            }
                        }));
                        success++;
                        done = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 提取具体错误信息
                        string errMsg = ex.Message;
                        if (ex is WebException we)
                        {
                            if (we.Response is HttpWebResponse wr)
                                errMsg = $"HTTP {(int)wr.StatusCode} {wr.StatusDescription}";
                            else if (we.Status == WebExceptionStatus.ConnectFailure)
                                errMsg = "连接失败，NAS 不可达";
                            else if (we.Status == WebExceptionStatus.Timeout)
                                errMsg = "连接超时";
                            else
                                errMsg = $"网络错误: {we.Status}";
                        }

                        if (attempt < 3)
                        {
                            string retryMsg = errMsg;
                            int retryI = capturedI;
                            BeginInvoke(new Action(() =>
                            {
                                if (retryI < lstFiles.Items.Count)
                                {
                                    lstFiles.Items[retryI].SubItems[2].Text = $"重试{attempt}({retryMsg})";
                                    lstFiles.Items[retryI].SubItems[2].ForeColor = Color.Yellow;
                                }
                            }));
                            await Task.Delay(1500, ct);
                        }
                        else
                        {
                            string finalErr = errMsg;
                            int finalI = capturedI;
                            BeginInvoke(new Action(() =>
                            {
                                if (finalI < lstFiles.Items.Count)
                                {
                                    lstFiles.Items[finalI].SubItems[2].Text = $"失败: {finalErr}";
                                    lstFiles.Items[finalI].SubItems[2].ForeColor = Color.Red;
                                }
                            }));
                            fail++;
                        }
                    }
                }
            }

            int s = success, sk = skip, f = fail;
            BeginInvoke(new Action(() =>
            {
                _isUploading = false;
                progressBar.Value = 100;
                lblStatus.Text = $"完成 成功:{s} 跳过:{sk} 失败:{f}";
                MessageBox.Show($"上传完成\n成功:{s}  跳过:{sk}  失败:{f}", "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
        }

        private async Task<bool> RemoteFileExistsAsync(string url)
        {
            try
            {
                var req = WebRequest.CreateHttp(url);
                req.Method = "HEAD";
                req.Credentials = new NetworkCredential(UserName, Pwd);
                req.Timeout = 8000;
                using (var resp = await req.GetResponseAsync())
                    return true;
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse r && r.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
            catch { return false; }
        }

        private async Task UploadFileAsync(string localPath, string remoteUrl, long fileSize, CancellationToken ct)
        {
            const int BufSize = 256 * 1024;
            byte[] buf = new byte[BufSize];

            var req = WebRequest.CreateHttp(remoteUrl);
            req.Method = "PUT";
            req.Credentials = new NetworkCredential(UserName, Pwd);
            req.ContentLength = fileSize;
            req.Timeout = 120_000;
            req.ReadWriteTimeout = 300_000;
            req.PreAuthenticate = true; // 直接发认证头，避免 401 往返

            using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read))
            using (var rs = await Task.Run(() => req.GetRequestStream(), ct))
            {
                int read;
                while ((read = await fs.ReadAsync(buf, 0, BufSize, ct)) > 0)
                {
                    if (ct.IsCancellationRequested) break;
                    await rs.WriteAsync(buf, 0, read, ct);
                }
            }

            using (var resp = (HttpWebResponse)await Task.Run(() => req.GetResponse(), ct))
            {
                var sc = resp.StatusCode;
                if (sc != HttpStatusCode.Created && sc != HttpStatusCode.NoContent && sc != HttpStatusCode.OK)
                    throw new Exception($"服务器返回: {(int)sc} {sc}");
            }
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024:F1} MB";
            return $"{bytes / 1024.0 / 1024 / 1024:F2} GB";
        }
    }
}
