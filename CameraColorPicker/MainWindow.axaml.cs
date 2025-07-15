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

        /// <summary>
        /// 检测框的中心位置（相对于图像坐标）
        /// </summary>
        private (double X, double Y) _selectionCenter = (320, 240); // 默认中心位置

        /// <summary>
        /// 检测框大小（像素）
        /// </summary>
        private const int SelectionSize = 100;

        /// <summary>
        /// 是否允许拖拽检测框
        /// </summary>
        private bool _isDragging = false;

        #endregion

        /// <summary>
        /// 主窗口构造函数
        /// 初始化UI组件、定时器、串口管理器和输入事件
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

            // 绑定输入事件（鼠标和触摸）
            InitializeInputEvents();
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

        #region 输入控制相关方法

        /// <summary>
        /// 初始化输入事件（鼠标和触摸）
        /// </summary>
        private void InitializeInputEvents()
        {
            // 当界面加载完成后绑定事件
            this.Loaded += (sender, e) =>
            {
                if (CameraImage != null)
                {
                    // 绑定鼠标事件
                    CameraImage.PointerPressed += OnImagePointerPressed;
                    CameraImage.PointerMoved += OnImagePointerMoved;
                    CameraImage.PointerReleased += OnImagePointerReleased;

                    // 启用鼠标事件
                    CameraImage.IsHitTestVisible = true;
                }

                // 绑定键盘事件
                this.KeyDown += OnKeyDown;
            };
        }

        /// <summary>
        /// 鼠标/触摸按下事件处理
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void OnImagePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            try
            {
                var position = e.GetPosition(CameraImage);
                var imageCoord = ConvertUIToImageCoordinates(position);

                if (imageCoord.HasValue)
                {
                    // 检查是否点击在检测框附近（允许拖拽）
                    var distance = Math.Sqrt(
                        Math.Pow(imageCoord.Value.X - _selectionCenter.X, 2) +
                        Math.Pow(imageCoord.Value.Y - _selectionCenter.Y, 2));

                    if (distance <= SelectionSize / 2 + 20) // 在检测框内或边缘附近
                    {
                        _isDragging = true;
                        CameraImage.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                    }
                    else
                    {
                        // 直接移动检测框到点击位置
                        UpdateSelectionCenter(imageCoord.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PointerPressed error: {ex.Message}");
            }
        }

        /// <summary>
        /// 鼠标/触摸移动事件处理
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void OnImagePointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
        {
            try
            {
                if (_isDragging)
                {
                    var position = e.GetPosition(CameraImage);
                    var imageCoord = ConvertUIToImageCoordinates(position);

                    if (imageCoord.HasValue)
                    {
                        UpdateSelectionCenter(imageCoord.Value);
                    }
                }
                else
                {
                    // 检查鼠标是否在检测框附近，更新光标样式
                    var position = e.GetPosition(CameraImage);
                    var imageCoord = ConvertUIToImageCoordinates(position);

                    if (imageCoord.HasValue)
                    {
                        var distance = Math.Sqrt(
                            Math.Pow(imageCoord.Value.X - _selectionCenter.X, 2) +
                            Math.Pow(imageCoord.Value.Y - _selectionCenter.Y, 2));

                        if (distance <= SelectionSize / 2 + 20)
                        {
                            CameraImage.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                        }
                        else
                        {
                            CameraImage.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PointerMoved error: {ex.Message}");
            }
        }

        /// <summary>
        /// 鼠标/触摸释放事件处理
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void OnImagePointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
            try
            {
                _isDragging = false;
                CameraImage.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PointerReleased error: {ex.Message}");
            }
        }

        /// <summary>
        /// 将UI坐标转换为图像坐标
        /// </summary>
        /// <param name="uiPosition">UI坐标</param>
        /// <returns>图像坐标，如果无效则返回null</returns>
        private (double X, double Y)? ConvertUIToImageCoordinates(Avalonia.Point uiPosition)
        {
            try
            {
                // 获取当前帧信息
                WriteableBitmap? currentFrame = null;
                lock (_frameLock)
                {
                    currentFrame = _currentFrame;
                }

                if (currentFrame == null) return null;

                // 获取图像控件的实际显示尺寸
                var imageActualWidth = CameraImage.Bounds.Width;
                var imageActualHeight = CameraImage.Bounds.Height;

                if (imageActualWidth <= 0 || imageActualHeight <= 0) return null;

                // 计算缩放比例（保持宽高比）
                var frameWidth = currentFrame.PixelSize.Width;
                var frameHeight = currentFrame.PixelSize.Height;
                var scaleX = imageActualWidth / frameWidth;
                var scaleY = imageActualHeight / frameHeight;
                var scale = Math.Min(scaleX, scaleY);

                // 计算实际显示的图像尺寸
                var displayWidth = frameWidth * scale;
                var displayHeight = frameHeight * scale;

                // 计算居中偏移量
                var offsetX = (imageActualWidth - displayWidth) / 2;
                var offsetY = (imageActualHeight - displayHeight) / 2;

                // 检查点击是否在图像显示区域内
                if (uiPosition.X < offsetX || uiPosition.X > offsetX + displayWidth ||
                    uiPosition.Y < offsetY || uiPosition.Y > offsetY + displayHeight)
                {
                    return null;
                }

                // 转换为图像坐标
                var imageX = (uiPosition.X - offsetX) / scale;
                var imageY = (uiPosition.Y - offsetY) / scale;

                // 确保坐标在图像范围内
                imageX = Math.Max(SelectionSize / 2, Math.Min(frameWidth - SelectionSize / 2, imageX));
                imageY = Math.Max(SelectionSize / 2, Math.Min(frameHeight - SelectionSize / 2, imageY));

                return (imageX, imageY);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 更新检测框中心位置
        /// </summary>
        /// <param name="newCenter">新的中心位置（图像坐标）</param>
        private void UpdateSelectionCenter((double X, double Y) newCenter)
        {
            _selectionCenter = newCenter;

            // 立即更新UI显示
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 强制更新覆盖层
                WriteableBitmap? currentFrame = null;
                lock (_frameLock)
                {
                    currentFrame = _currentFrame;
                }

                if (currentFrame != null)
                {
                    UpdateOverlay(currentFrame.PixelSize.Width, currentFrame.PixelSize.Height);

                    // 立即提取新位置的RGB值
                    var rgb = ExtractRgbFromBitmap(currentFrame, (int)_selectionCenter.X, (int)_selectionCenter.Y, SelectionSize);
                    var grayscale = (byte)(0.299 * rgb.R + 0.587 * rgb.G + 0.114 * rgb.B);

                    // 更新显示
                    RgbText.Text = $"RGB: {rgb.R}, {rgb.G}, {rgb.B}";
                    GrayscaleText.Text = $"Gray: {grayscale}";

                    // 发送串口数据
                    Task.Run(() => _serialManager?.SendColorDataAsync(rgb.R, rgb.G, rgb.B, grayscale));
                }
            });
        }

        /// <summary>
        /// 键盘按键事件处理
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">事件参数</param>
        private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
        {
            try
            {
                switch (e.Key)
                {
                    case Avalonia.Input.Key.Space:
                    case Avalonia.Input.Key.C:
                        // 空格键或C键：重置检测框到中心
                        ResetSelectionToCenter();
                        e.Handled = true;
                        break;

                    case Avalonia.Input.Key.Up:
                        // 上箭头：向上移动检测框
                        MoveSelection(0, -10);
                        e.Handled = true;
                        break;

                    case Avalonia.Input.Key.Down:
                        // 下箭头：向下移动检测框
                        MoveSelection(0, 10);
                        e.Handled = true;
                        break;

                    case Avalonia.Input.Key.Left:
                        // 左箭头：向左移动检测框
                        MoveSelection(-10, 0);
                        e.Handled = true;
                        break;

                    case Avalonia.Input.Key.Right:
                        // 右箭头：向右移动检测框
                        MoveSelection(10, 0);
                        e.Handled = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"KeyDown error: {ex.Message}");
            }
        }

        /// <summary>
        /// 重置检测框到图像中心
        /// </summary>
        private void ResetSelectionToCenter()
        {
            WriteableBitmap? currentFrame = null;
            lock (_frameLock)
            {
                currentFrame = _currentFrame;
            }

            if (currentFrame != null)
            {
                var centerX = currentFrame.PixelSize.Width / 2.0;
                var centerY = currentFrame.PixelSize.Height / 2.0;
                UpdateSelectionCenter((centerX, centerY));
            }
        }

        /// <summary>
        /// 相对移动检测框位置
        /// </summary>
        /// <param name="deltaX">X方向移动距离</param>
        /// <param name="deltaY">Y方向移动距离</param>
        private void MoveSelection(double deltaX, double deltaY)
        {
            WriteableBitmap? currentFrame = null;
            lock (_frameLock)
            {
                currentFrame = _currentFrame;
            }

            if (currentFrame != null)
            {
                var newX = _selectionCenter.X + deltaX;
                var newY = _selectionCenter.Y + deltaY;

                // 边界检查
                newX = Math.Max(SelectionSize / 2, Math.Min(currentFrame.PixelSize.Width - SelectionSize / 2, newX));
                newY = Math.Max(SelectionSize / 2, Math.Min(currentFrame.PixelSize.Height - SelectionSize / 2, newY));

                UpdateSelectionCenter((newX, newY));
            }
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

                // 设置默认检测框位置为图像中心（640x480）
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _selectionCenter = (320, 240);
                });
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

                    // 提取检测框区域的RGB值并显示
                    var rgb = ExtractRgbFromBitmap(frameCopy, (int)_selectionCenter.X, (int)_selectionCenter.Y, SelectionSize);

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

            // 根据当前检测框位置放置矩形框
            var rectSize = SelectionSize;
            var rectX = (_selectionCenter.X * scale) - (rectSize / 2);
            var rectY = (_selectionCenter.Y * scale) - (rectSize / 2);

            // 确保矩形框在显示区域内
            rectX = Math.Max(0, Math.Min(displayWidth - rectSize, rectX));
            rectY = Math.Max(0, Math.Min(displayHeight - rectSize, rectY));

            Canvas.SetLeft(SelectionRect, rectX);
            Canvas.SetTop(SelectionRect, rectY);

            // 智能计算RGB显示框的位置
            var rgbDisplayWidth = 120;  // RGB显示框的大概宽度
            var rgbDisplayHeight = 80;  // RGB显示框的大概高度
            var lineLength = 100;       // 指示线长度
            var lineOffset = 50;        // 指示线垂直偏移量

            // 计算矩形框的中心点
            var rectCenterX = rectX + rectSize / 2;
            var rectCenterY = rectY + rectSize / 2;

            // 默认位置：右上方
            var lineStartX = rectX + rectSize;
            var lineStartY = rectCenterY;
            var lineEndX = lineStartX + lineLength;
            var lineEndY = lineStartY - lineOffset;
            var rgbDisplayX = lineEndX;
            var rgbDisplayY = lineEndY - rgbDisplayHeight / 2;

            // 检查右上方是否有足够空间
            if (lineEndX + rgbDisplayWidth > displayWidth || lineEndY - rgbDisplayHeight / 2 < 0)
            {
                // 尝试左上方
                lineStartX = rectX;
                lineStartY = rectCenterY;
                lineEndX = lineStartX - lineLength;
                lineEndY = lineStartY - lineOffset;
                rgbDisplayX = lineEndX - rgbDisplayWidth;
                rgbDisplayY = lineEndY - rgbDisplayHeight / 2;

                // 检查左上方是否有足够空间
                if (lineEndX - rgbDisplayWidth < 0 || lineEndY - rgbDisplayHeight / 2 < 0)
                {
                    // 尝试右下方
                    lineStartX = rectX + rectSize;
                    lineStartY = rectCenterY;
                    lineEndX = lineStartX + lineLength;
                    lineEndY = lineStartY + lineOffset;
                    rgbDisplayX = lineEndX;
                    rgbDisplayY = lineEndY - rgbDisplayHeight / 2;

                    // 检查右下方是否有足够空间
                    if (lineEndX + rgbDisplayWidth > displayWidth || lineEndY + rgbDisplayHeight / 2 > displayHeight)
                    {
                        // 尝试左下方
                        lineStartX = rectX;
                        lineStartY = rectCenterY;
                        lineEndX = lineStartX - lineLength;
                        lineEndY = lineStartY + lineOffset;
                        rgbDisplayX = lineEndX - rgbDisplayWidth;
                        rgbDisplayY = lineEndY - rgbDisplayHeight / 2;

                        // 检查左下方是否有足够空间
                        if (lineEndX - rgbDisplayWidth < 0 || lineEndY + rgbDisplayHeight / 2 > displayHeight)
                        {
                            // 如果四个角都不合适，则使用最接近的可用位置
                            // 优先选择水平方向有空间的位置
                            if (rectCenterX < displayWidth / 2)
                            {
                                // 检测框在左半部分，显示框放在右侧
                                lineStartX = rectX + rectSize;
                                lineStartY = rectCenterY;
                                lineEndX = Math.Min(lineStartX + lineLength, displayWidth - rgbDisplayWidth - 10);
                                lineEndY = rectCenterY;
                                rgbDisplayX = lineEndX;
                                rgbDisplayY = Math.Max(10, Math.Min(displayHeight - rgbDisplayHeight - 10, rectCenterY - rgbDisplayHeight / 2));
                            }
                            else
                            {
                                // 检测框在右半部分，显示框放在左侧
                                lineStartX = rectX;
                                lineStartY = rectCenterY;
                                lineEndX = Math.Max(lineStartX - lineLength, rgbDisplayWidth + 10);
                                lineEndY = rectCenterY;
                                rgbDisplayX = lineEndX - rgbDisplayWidth;
                                rgbDisplayY = Math.Max(10, Math.Min(displayHeight - rgbDisplayHeight - 10, rectCenterY - rgbDisplayHeight / 2));
                            }
                        }
                    }
                }
            }

            // 最终边界检查，确保RGB显示框完全在可视区域内
            rgbDisplayX = Math.Max(0, Math.Min(displayWidth - rgbDisplayWidth, rgbDisplayX));
            rgbDisplayY = Math.Max(0, Math.Min(displayHeight - rgbDisplayHeight, rgbDisplayY));

            // 根据最终的RGB显示框位置调整指示线终点
            var finalRgbCenterX = rgbDisplayX + rgbDisplayWidth / 2;
            var finalRgbCenterY = rgbDisplayY + rgbDisplayHeight / 2;

            // 计算从矩形框中心到RGB显示框中心的指示线
            var deltaX = finalRgbCenterX - rectCenterX;
            var deltaY = finalRgbCenterY - rectCenterY;
            var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

            if (distance > 0)
            {
                // 标准化方向向量
                var dirX = deltaX / distance;
                var dirY = deltaY / distance;

                // 指示线起点：矩形框边缘
                lineStartX = rectCenterX + dirX * (rectSize / 2);
                lineStartY = rectCenterY + dirY * (rectSize / 2);

                // 指示线终点：RGB显示框边缘
                var rgbEdgeDistance = Math.Min(rgbDisplayWidth, rgbDisplayHeight) / 2;
                lineEndX = finalRgbCenterX - dirX * rgbEdgeDistance;
                lineEndY = finalRgbCenterY - dirY * rgbEdgeDistance;
            }

            // 设置指示线的起点和终点
            IndicatorLine.StartPoint = new Avalonia.Point(lineStartX, lineStartY);
            IndicatorLine.EndPoint = new Avalonia.Point(lineEndX, lineEndY);

            // 将RGB显示框放置在计算出的最佳位置
            Canvas.SetLeft(RgbDisplay, rgbDisplayX);
            Canvas.SetTop(RgbDisplay, rgbDisplayY);
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