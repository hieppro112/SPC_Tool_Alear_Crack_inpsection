using System.Configuration;
using System.Data;
using System.Windows;

namespace ToolAleart_Inspection
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private MainWindow? _main;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Tạo cửa sổ nhưng KHÔNG Show: tool chạy ngầm, chỉ hiện khi cần cảnh báo
            _main = new MainWindow();
        }
    }

}
