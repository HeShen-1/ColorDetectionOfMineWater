#nullable enable

using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CameraColorPicker
{
    /// <summary>
    /// 主窗口类，负责摄像头显示和颜色提取功能
    /// 继承自Avalonia的Window类，实现了完整的摄像头颜色提取器UI逻辑
    /// </summary>
    public partial class MainWindow : Avalonia.Controls.Window
    {
        #region 私有字段

        /// <summary>
        /// V4L2摄像头接口实例，用于捕获摄像头画面
        /// </summary>
        private V4L2CameraSafe? _camera;

        /// <summary>
        /// 串口通信管理器
        /// </summary>
        private SerialPortManager? _serialManager;

        /// <summary>
        /// 取消令牌源，用于控制异步任务的取消
        /// </summary>
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// FPS计算定时器，每秒更新一次帧率显示
        /// </summary>
        private readonly DispatcherTimer _fpsTimer;

        /// <summary>
        /// 帧计数器，用于计算FPS
        /// </summary>
        private int _frameCount;

        /// <summary>
        /// 上次FPS更新的时间戳
        /// </summary>
        private DateTime _lastFpsUpdate;

        /// <summary>
        /// 当前帧的位图数据
        /// </summary>
        private WriteableBitmap? _currentFrame;

        /// <summary>
        /// 帧数据的线程锁，确保多线程安全访问
        /// </summary>
        private readonly object _frameLock = new object();

        /// <summary>
        /// 当前检测到的RGB值
        /// </summary>
        private (byte R, byte G, byte B) _currentRgb = (0, 0, 0);

        /// <summary>
        /// 当前检测到的灰度值
        /// </summary>
        private byte _currentGrayscale = 0;

        #endregion

        /// <summary>
        /// 主窗口构造函数
        /// 初始化UI组件、定时器和串口管理器
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // 初始化FPS计算定时器
            _fpsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)  // 每秒触发一次
            };
            _fpsTimer.Tick += UpdateFps;
            _lastFpsUpdate = DateTime.Now;

            // 初始化串口管理器
            _serialManager = new SerialPortManager();
            _serialManager.ConnectionStatusChanged += OnSerialConnectionChanged;
            _serialManager.DataSent += OnSerialDataSent;

            // 绑定窗口事件
            Opened += OnWindowOpened;
            Closing += OnWindowClosing;
        }

        #region 窗口事件处理

        /// <summary>
        /// 窗口打开事件处理程序
        /// 在窗口打开后异步初始化摄像头和串口
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void OnWindowOpened(object? sender, EventArgs e)
        {
            Task.Run(() => InitializeCamera());
            Task.Run(() => InitializeSerial());
        }

        /// <summary>
        /// 窗口关闭事件处理程序
        /// 在窗口关闭时停止摄像头和串口通信
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            StopCamera();
            _serialManager?.Dispose();
        }

        #endregion

        #region 串口通信相关方法

        /// <summary>
        /// 初始化串口通信
        /// 尝试连接常见的串口设备
        /// </summary>
        private async Task InitializeSerial()
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText.Text = "Status: Initializing serial port...";
                });

                // 常见的树莓派串口设备路径
                string[] commonPorts = {
                    "/dev/ttyUSB0",     // USB转串口设备
                    "/dev/ttyACM0",     // USB CDC设备
                    "/dev/ttyAMA0",     // 树莓派硬件串口
                    "/dev/serial0",     // 树莓派主串口别名
                    "/dev/ttyS0"        // 标准串口
                };

                bool connected = false;
                foreach (var port in commonPorts)
                {
                    if (_serialManager?.OpenPort(port, 9600) == true)
                    {
                        connected = true;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            SerialStatusText.Text = $"Serial: Connected ({port})";
                        });
                        break;
                    }
                }

                if (!connected)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        SerialStatusText.Text = "Serial: No device found";
                    });
                }
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SerialStatusText.Text = $"Serial: Error - {ex.Message}";
                });
            }
        }

        /// <summary>
        /// 串口连接状态变化事件处理
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="isConnected">是否已连接</param>
        private void OnSerialConnectionChanged(object? sender, bool isConnected)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (isConnected)
                {
                    SerialStatusText.Text = "Serial: Connected";
                }
                else
                {
                    SerialStatusText.Text = "Serial: Disconnected";
                }
            });
        }

        /// <summary>
        /// 串口数据发送事件处理
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="data">发送的数据</param>
        private void OnSerialDataSent(object? sender, string data)
        {
            // 可以在这里记录发送的数据或显示在UI上
            Debug.WriteLine($"串口数据已发送: {data}");
        }

        #endregion

        #region 摄像头初始化和控制

        /// <summary>
        /// 异步初始化摄像头
        /// 尝试打开不同的视频设备，如果都失败则使用测试模式
        /// </summary>
        private async Task InitializeCamera()
        {
            try
            {
                // 更新状态显示
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText.Text = "Status: Initializing camera...";
                });

                // 创建摄像头实例
                _camera = new V4L2CameraSafe();

                // 尝试打开不同的视频设备（/dev/video0 到 /dev/video9）
                bool opened = false;
                for (int i = 0; i < 10; i++)
                {
                    if (_camera.Open($"/dev/video{i}"))
                    {
                        opened = true;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            StatusText.Text = $"Status: Camera connected (/dev/video{i})";
                        });
                        break;
                    }
                }

                if (!opened)
                {
                    // 如果没有找到摄像头，使用测试图案模式
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        StatusText.Text = "Status: Using test pattern (no camera)";
                    });
                }

                // 启动FPS计时器
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _fpsTimer.Start();
                });

                // 启动帧捕获循环
                _cancellationTokenSource = new CancellationTokenSource();
                _ = Task.Run(() => CaptureLoop(_cancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                // 处理初始化异常
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText.Text = $"Status: Error - {ex.Message}";
                });
            }
        }

        /// <summary>
        /// 摄像头帧捕获循环
        /// 在后台线程中持续捕获摄像头帧并更新UI
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        private async Task CaptureLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _camera != null)
            {
                try
                {
                    // 捕获一帧图像
                    var frame = await _camera.CaptureFrameAsync();
                    if (frame != null)
                    {
                        // 线程安全地更新当前帧
                        lock (_frameLock)
                        {
                            _currentFrame = frame;
                        }

                        // 在UI线程中更新界面
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            UpdateUI();
                        });

                        _frameCount++;
                    }

                    // 控制帧率为20FPS（每50毫秒一帧）
                    await Task.Delay(50, cancellationToken);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Capture error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 停止摄像头
        /// 取消所有异步任务并释放资源
        /// </summary>
        private void StopCamera()
        {
            // 取消异步任务
            _cancellationTokenSource?.Cancel();
            _fpsTimer.Stop();

            // 清理当前帧数据
            lock (_frameLock)
            {
                _currentFrame = null;
            }

            // 释放摄像头资源
            _camera?.Dispose();
            _camera = null;
        }

        #endregion

        #region UI更新

        /// <summary>
        /// 更新用户界面
        /// 显示最新的摄像头帧并提取RGB颜色值，同时发送串口数据
        /// </summary>
        private void UpdateUI()
        {
            try
            {
                WriteableBitmap? frameCopy = null;

                // 线程安全地获取当前帧
                lock (_frameLock)
                {
                    frameCopy = _currentFrame;
                }

                if (frameCopy != null)
                {
                    // 更新摄像头图像显示
                    CameraImage.Source = frameCopy;

                    // 更新覆盖层位置（矩形框和指示线）
                    UpdateOverlay(frameCopy.PixelSize.Width, frameCopy.PixelSize.Height);

                    // 提取中心区域的RGB值并显示
                    var centerX = frameCopy.PixelSize.Width / 2;
                    var centerY = frameCopy.PixelSize.Height / 2;
                    var rgb = ExtractRgbFromBitmap(frameCopy, centerX, centerY, 100);

                    // 计算灰度值 (使用标准的灰度转换公式)
                    var grayscale = (byte)(0.299 * rgb.R + 0.587 * rgb.G + 0.114 * rgb.B);

                    // 更新当前RGB和灰度值
                    _currentRgb = rgb;
                    _currentGrayscale = grayscale;

                    // 更新UI显示
                    RgbText.Text = $"RGB: {rgb.R}, {rgb.G}, {rgb.B}";
                    GrayscaleText.Text = $"Gray: {grayscale}";

                    // 发送串口数据
                    Task.Run(() => _serialManager?.SendColorDataAsync(rgb.R, rgb.G, rgb.B, grayscale));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateUI error: {ex.Message}");
            }
        }

        /// <summary>
        /// 从位图中提取指定区域的平均RGB值
        /// 计算检测框内所有像素的RGB平均值
        /// </summary>
        /// <param name="bitmap">源位图</param>
        /// <param name="centerX">中心点X坐标</param>
        /// <param name="centerY">中心点Y坐标</param>
        /// <param name="size">检测框大小（像素）</param>
        /// <returns>平均RGB值</returns>
        private (byte R, byte G, byte B) ExtractRgbFromBitmap(WriteableBitmap bitmap, int centerX, int centerY, int size)
        {
            try
            {
                int r = 0, g = 0, b = 0;
                int count = 0;

                // 锁定位图数据进行像素级访问
                using (var buffer = bitmap.Lock())
                {
                    unsafe
                    {
                        // 获取像素数据指针和行步长
                        var ptr = (byte*)buffer.Address.ToPointer();
                        var stride = buffer.RowBytes;

                        // 计算检测区域边界
                        int startX = Math.Max(0, centerX - size / 2);
                        int endX = Math.Min(bitmap.PixelSize.Width, centerX + size / 2);
                        int startY = Math.Max(0, centerY - size / 2);
                        int endY = Math.Min(bitmap.PixelSize.Height, centerY + size / 2);

                        // 遍历检测区域内的所有像素
                        for (int y = startY; y < endY; y++)
                        {
                            for (int x = startX; x < endX; x++)
                            {
                                // 计算像素在内存中的偏移量（RGB24格式，每像素3字节）
                                var offset = y * stride + x * 3;
                                r += ptr[offset];       // 红色分量
                                g += ptr[offset + 1];   // 绿色分量
                                b += ptr[offset + 2];   // 蓝色分量
                                count++;
                            }
                        }
                    }
                }

                // 计算平均值
                if (count > 0)
                {
                    return ((byte)(r / count), (byte)(g / count), (byte)(b / count));
                }

                return (0, 0, 0);
            }
            catch
            {
                // 异常情况返回黑色
                return (0, 0, 0);
            }
        }

        /// <summary>
        /// 更新覆盖层元素的位置
        /// 根据图像的实际显示尺寸调整矩形框、指示线和RGB显示框的位置
        /// </summary>
        /// <param name="frameWidth">帧宽度</param>
        /// <param name="frameHeight">帧高度</param>
        private void UpdateOverlay(int frameWidth, int frameHeight)
        {
            // 获取图像控件的实际显示尺寸
            var imageActualWidth = CameraImage.Bounds.Width;
            var imageActualHeight = CameraImage.Bounds.Height;

            if (imageActualWidth <= 0 || imageActualHeight <= 0) return;

            // 计算缩放比例（保持宽高比）
            var scaleX = imageActualWidth / frameWidth;
            var scaleY = imageActualHeight / frameHeight;
            var scale = Math.Min(scaleX, scaleY);

            // 计算实际显示的图像尺寸
            var displayWidth = frameWidth * scale;
            var displayHeight = frameHeight * scale;

            // 计算居中偏移量
            var offsetX = (imageActualWidth - displayWidth) / 2;
            var offsetY = (imageActualHeight - displayHeight) / 2;

            // 更新覆盖层画布的尺寸和位置
            OverlayCanvas.Width = displayWidth;
            OverlayCanvas.Height = displayHeight;
            Canvas.SetLeft(OverlayCanvas, offsetX);
            Canvas.SetTop(OverlayCanvas, offsetY);

            // 在画面中心放置检测矩形框
            var rectSize = 100;
            var rectX = (displayWidth - rectSize) / 2;
            var rectY = (displayHeight - rectSize) / 2;

            Canvas.SetLeft(SelectionRect, rectX);
            Canvas.SetTop(SelectionRect, rectY);

            // 设置指示线和RGB显示框位置
            var lineStartX = rectX + rectSize;          // 线条起点：矩形右边
            var lineStartY = rectY + rectSize / 2;      // 线条起点：矩形中间高度
            var lineEndX = lineStartX + 100;            // 线条终点：向右延伸100像素
            var lineEndY = lineStartY - 50;             // 线条终点：向上偏移50像素

            // 设置指示线的起点和终点
            IndicatorLine.StartPoint = new Avalonia.Point(lineStartX, lineStartY);
            IndicatorLine.EndPoint = new Avalonia.Point(lineEndX, lineEndY);

            // 将RGB显示框放置在指示线终点附近
            Canvas.SetLeft(RgbDisplay, lineEndX);
            Canvas.SetTop(RgbDisplay, lineEndY - 40);
        }

        /// <summary>
        /// 更新FPS显示
        /// 定时器每秒调用一次，计算并显示当前帧率
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void UpdateFps(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;
            var fps = _frameCount / elapsed;

            // 更新FPS显示文本
            FpsText.Text = $"FPS: {fps:F1}";

            // 重置计数器
            _frameCount = 0;
            _lastFpsUpdate = now;
        }

        #endregion
    }
}