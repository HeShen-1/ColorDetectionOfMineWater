using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace CameraColorPicker
{
    /// <summary>
    /// 应用程序主类
    /// 继承自Avalonia.Application，负责应用程序的初始化和生命周期管理
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 初始化应用程序
        /// 加载XAML资源和配置
        /// </summary>
        public override void Initialize()
        {
            // 加载应用程序的XAML资源（样式、模板等）
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>
        /// 框架初始化完成后的回调方法
        /// 在此方法中创建并设置主窗口
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            // 检查是否为桌面应用程序生命周期
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // 创建并设置主窗口实例
                desktop.MainWindow = new MainWindow();
            }

            // 调用基类方法完成初始化
            base.OnFrameworkInitializationCompleted();
        }
    }
}