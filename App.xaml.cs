using System.Text;
using System.Windows;

namespace CHMGenerator.WPF;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 注册 GB2312 编码支持
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        base.OnStartup(e);
    }
}
