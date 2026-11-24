using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ToolAleart_Inspection.Database;
using ToolAleart_Inspection.Model;

namespace ToolAleart_Inspection
{
    /// <summary>
    /// Chạy ngầm, kiểm tra liên tục PO hiện tại của máy.
    /// PO có công đoạn Crack Check mà isCrack = true hoặc chưa có kết quả (null)
    /// → tắt WorkSupportSystem NGAY khi phát hiện + hiện cảnh báo TOÀN MÀN HÌNH,
    ///   chỉ đóng khi người dùng bấm OK (sau đếm ngược) hoặc hết thời gian AlertDuration.
    /// Bình thường → ẩn cửa sổ.
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

        // Thời gian tối đa hiển thị cảnh báo: hết thời gian thì tự đóng
        private static readonly TimeSpan AlertDuration = TimeSpan.FromSeconds(7);

        // Nút OK chỉ bấm được sau khi đếm ngược xong
        private static readonly TimeSpan OkCountdown = TimeSpan.FromSeconds(5);

        private static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(500);

        // Tên tiến trình cần tắt: cột "Name" trong Task Manager > Details, BỎ đuôi ".exe"
        private const string BlockedProcessName = "WorkSupportSystem";

        // Mã máy: tham số dòng lệnh thứ nhất (vd: ToolAleart_Inspection.exe F2MNPC250023),
        // không truyền thì lấy tên máy tính hiện tại (giống QAToolcheckCamera)
        private readonly string _pcName;

        private readonly CancellationTokenSource _cts = new();
        private readonly DispatcherTimer _blinkTimer;
        private DateTime _alertStartedAt;
        private bool _alertShowing;
        private bool _blinkOn;

        public MainWindow()
        {
            InitializeComponent();

            string[] args = Environment.GetCommandLineArgs();
            _pcName = (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1])
                        ? args[1]
                        : Dns.GetHostName()).Trim().ToUpperInvariant();
            //_pcName = "F2MNPC250042";
            txtPc.Text = _pcName;

            _blinkTimer = new DispatcherTimer { Interval = BlinkInterval };
            _blinkTimer.Tick += BlinkTimer_Tick;

            // Chặn Alt+F4: cảnh báo chỉ đóng bằng nút OK hoặc khi hết thời gian
            Closing += (_, e) => e.Cancel = true;

            _ = PollLoopAsync(_cts.Token);
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            try
            {
                using var timer = new PeriodicTimer(PollInterval);
                do
                {
                    await CheckOnceAsync(ct);
                }
                while (await timer.WaitForNextTickAsync(ct));
            }
            catch (OperationCanceledException)
            {
                // app thoát → dừng vòng lặp
            }
        }

        private async Task CheckOnceAsync(CancellationToken ct)
        {
            try
            {
                //kiem tra PO hiện tại của máy, có bị crack hay chưa
                CrackStatus? status = await ISql.GetCrackStatusAsync(_pcName, ct);
                if(await ISql.CheckprocessCrack(status?.Po ?? string.Empty, ct))
                {
                    ApplyStatus(status);
                    txtError.Text = string.Empty;
                }
                else
                {
                    CloseAlert();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Lỗi mạng/SQL: không chặn, lần sau thử lại
                txtError.Text = "Lỗi kết nối: " + ex.Message;
            }

            txtUpdated.Text = "Cập nhật lúc: " + DateTime.Now.ToString("HH:mm:ss dd/MM/yyyy");
        }

        private void ApplyStatus(CrackStatus? s)
        {
            txtMachine.Text = s?.ProcessName ?? "-";
            txtPo.Text = s?.Po ?? "-";

            // Chặn khi PO bị crack (true) hoặc chưa có kết quả kiểm tra (null)
            bool blocked = s != null && s.Po != null && s.IsCrack != false;
            if (!blocked)
            {
                // PO đã OK → đóng cảnh báo (nếu đang hiện)
                CloseAlert();
                return;
            }

            // Tắt WorkSupportSystem ngay khi phát hiện (mỗi lần check mà PO còn bị chặn)
            KillBlockedProcess();

            string reason = s!.IsCrack == true
                ? "Video ≤ 30s (crack)"
                : "Chưa có kết quả kiểm tra video";
            txtStatus.Text = $"PO {s.Po}\nCHƯA QUA CÔNG ĐOẠN CRACK\n{reason}";

            // Đang hiện thì giữ nguyên (không reset đếm ngược), chưa hiện thì bật lên
            if (!_alertShowing)
                ShowAlert();
        }

        private void ShowAlert()
        {
            _alertShowing = true;
            _alertStartedAt = DateTime.Now;
            _blinkOn = false;
            UpdateCountdown();
            _blinkTimer.Start();

            Show();
            WindowState = WindowState.Maximized;
            Topmost = true;
            Activate();
        }

        private void BlinkTimer_Tick(object? sender, EventArgs e)
        {
            // Hết thời gian quy định → tự đóng
            if (DateTime.Now - _alertStartedAt >= AlertDuration)
            {
                CloseAlert();
                return;
            }

            _blinkOn = !_blinkOn;
            bdStatus.Background = _blinkOn ? Brushes.Gold : Brushes.Red;
            TextElement.SetForeground(bdStatus, _blinkOn ? Brushes.Black : Brushes.White);
            UpdateCountdown();
        }

        private void UpdateCountdown()
        {
            TimeSpan elapsed = DateTime.Now - _alertStartedAt;

            int okLeft = (int)Math.Ceiling((OkCountdown - elapsed).TotalSeconds);
            btnOk.IsEnabled = okLeft <= 0;
            btnOk.Content = okLeft > 0 ? $"OK ({okLeft})" : "OK";

            int closeLeft = Math.Max(0, (int)Math.Ceiling((AlertDuration - elapsed).TotalSeconds));
            txtCountdown.Text = $"Tự đóng sau {closeLeft} giây";
        }

        // Người dùng xác nhận → đóng cảnh báo
        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            CloseAlert();
        }

        private void CloseAlert()
        {
            _alertShowing = false;

            _blinkTimer.Stop();
            bdStatus.Background = Brushes.Red;
            TextElement.SetForeground(bdStatus, Brushes.White);
            Topmost = false;
            Hide();
        }

        /// <summary>Tắt mọi tiến trình WorkSupportSystem đang chạy. Trả về số tiến trình đã tắt.</summary>
        private static int KillBlockedProcess()
        {
            int killed = 0;
            foreach (Process p in Process.GetProcessesByName(BlockedProcessName))
            {
                using (p)
                {
                    try
                    {
                        p.Kill(entireProcessTree: true);
                        killed++;
                    }
                    catch (Exception)
                    {
                        // đã tự thoát, hoặc chạy quyền Administrator nên không đủ quyền tắt
                    }
                }
            }
            return killed;
        }
    }
}
