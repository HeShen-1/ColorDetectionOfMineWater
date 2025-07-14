using Avalonia;
using System;

namespace CameraColorPicker
{
    /// <summary>
    /// 摄像头颜色提取器应用程序的主入口点类
    /// 负责配置和启动Avalonia UI应用程序
    /// </summary>
    class Program
    {
        /// <summary>
        /// 应用程序的主入口点
        /// 使用STA（Single Thread Apartment）线程模型，这是UI应用程序的标准要求
        /// </summary>
        /// <param name="args">命令行参数</param>
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        /// <summary>
        /// 构建Avalonia应用程序的配置
        /// 配置平台检测和日志输出到调试跟踪
        /// </summary>
        /// <returns>配置好的AppBuilder实例</returns>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()    // 自动检测运行平台（Windows/Linux/macOS）
                .LogToTrace();          // 将日志输出到系统调试跟踪
    }
}