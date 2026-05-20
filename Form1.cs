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
        // 固定配置（写在代码里）
        private static readonly string NasWebDavUrl = "http://10.201.2.31:5005/uploads/";
        private static readonly string UserName = "user";
        private static readonly string Pwd = "Cc880821/";

        // 拖拽区域
        private ListView lstFiles;
        private Label lblDropHint;

        // 状态栏
        private ProgressBar progressBar;
        private Label lblStatus;

        // 上传
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

            // 拖拽提示（覆盖在窗口上，空时显示）
            lblDropHint = new Label
            {
                Text = "将文件或文件夹拖拽到此处\n\n松开即开始上传",
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(100, 100, 100),
                Font = new Font("Segoe UI", 16)
            };
            this.Controls.Add(lblDropHint);

            // 文件列表
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
            lstFiles.Columns.Add("状态", 100);
            this.Controls.Add(lstFiles);

            // 状态栏
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
                lblDropHint.BackColor = Color.FromArgb(0, 120, 215, 40);
                lblDropHint.ForeColor = Color.FromArgb(180, 180, 180);
            }
        }

        private void MainForm_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Copy;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            lblDropHint.BackColor = Color.Transparent;
            lblDropHint.ForeColor = Color.FromArgb(100, 100, 100);

            string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            StartUpload(paths);
        }

        private void StartUpload(string[] paths)
        {
            if (_isUploading) return;

            // 展开所有文件
            var files = paths.SelectMany(p =>
                File.Exists(p) ? new[] { p } :
                Directory.Exists(p) ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories) :
                Array.Empty<string>()
            ).ToList();

            if (files.Count == 0) return;

            // 显示文件列表
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
                item.SubItems[2].ForeColor = Color.Gray;
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

                BeginInvoke(new Action(() =>
                {
                    lblStatus.Text = $"[{i + 1}/{files.Count}] {fileName}";
                    progressBar.Value = (i + 1) * 100 / files.Count;
                    if (i < lstFiles.Items.Count)
                    {
                        lstFiles.Items[i].SubItems[2].Text = "上传中";
                        lstFiles.Items[i].SubItems[2].ForeColor = Color.Orange;
                    }
                }));

                bool uploaded = false;

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        if (await RemoteFileExistsAsync(remoteUrl))
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (i < lstFiles.Items.Count)
                                {
                                    lstFiles.Items[i].SubItems[2].Text = "已存在";
                                    lstFiles.Items[i].SubItems[2].ForeColor = Color.Gray;
                                }
                            }));
                            skip++;
                            uploaded = true;
                            break;
                        }

                        await UploadFileAsync(localPath, remoteUrl, fileInfo.Length, ct);
                        BeginInvoke(new Action(() =>
                        {
                            if (i < lstFiles.Items.Count)
                            {
                                lstFiles.Items[i].SubItems[2].Text = "完成";
                                lstFiles.Items[i].SubItems[2].ForeColor = Color.Lime;
                            }
                        }));
                        success++;
                        uploaded = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt < 3)
                        {
                            await Task.Delay(1500, ct);
                        }
                        else
                        {
                            BeginInvoke(new Action(() =>
                            {
                                if (i < lstFiles.Items.Count)
                                {
                                    lstFiles.Items[i].SubItems[2].Text = "失败";
                                    lstFiles.Items[i].SubItems[2].ForeColor = Color.Red;
                                }
                            }));
                            fail++;
                        }
                    }
                }
            }

            BeginInvoke(new Action(() =>
            {
                _isUploading = false;
                progressBar.Value = 100;
                lblStatus.Text = $"完成 成功:{success} 跳过:{skip} 失败:{fail}";
                MessageBox.Show($"上传完成\n成功:{success}  跳过:{skip}  失败:{fail}", "完成",
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
                    throw new Exception($"服务器返回: {sc}");
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
