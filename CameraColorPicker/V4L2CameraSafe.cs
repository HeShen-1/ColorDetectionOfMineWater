#nullable enable

using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Diagnostics;
using System.IO;

namespace CameraColorPicker
{
    /// <summary>
    /// V4L2摄像头安全实现类
    /// 使用FFmpeg作为中间层来安全地访问摄像头设备，避免直接系统调用的兼容性问题
    /// 支持真实摄像头捕获和测试模式两种工作方式
    /// </summary>
    public class V4L2CameraSafe : IDisposable
    {
        #region 私有字段

        /// <summary>
        /// 图像宽度（像素）
        /// </summary>
        private int _width = 640;

        /// <summary>
        /// 图像高度（像素）
        /// </summary>
        private int _height = 480;

        /// <summary>
        /// 摄像头运行状态标志
        /// </summary>
        private bool _isRunning;

        /// <summary>
        /// 摄像头设备是否可用
        /// </summary>
        private bool _cameraAvailable = false;

        /// <summary>
        /// 摄像头设备路径
        /// </summary>
        private string? _devicePath;

        /// <summary>
        /// FFmpeg进程实例，用于捕获摄像头数据
        /// </summary>
        private Process? _ffmpegProcess;

        /// <summary>
        /// RGB帧数据缓冲区（每像素3字节）
        /// </summary>
        private byte[]? _frameBuffer;

        #endregion

        #region 公共方法

        /// <summary>
        /// 打开摄像头设备
        /// 首先检查设备可访问性，然后启动FFmpeg进程进行视频捕获
        /// </summary>
        /// <param name="devicePath">设备路径，默认为/dev/video0</param>
        /// <returns>是否成功打开设备</returns>
        public bool Open(string devicePath = "/dev/video0")
        {
            try
            {
                // 检查设备文件是否存在
                if (!File.Exists(devicePath))
                {
                    Debug.WriteLine($"Device {devicePath} does not exist");
                    return false;
                }

                // 尝试访问设备以验证权限和可用性
                try
                {
                    using (var fs = new FileStream(devicePath, FileMode.Open, FileAccess.Read))
                    {
                        // 如果能够打开，说明设备存在且可访问
                        _devicePath = devicePath;
                        _cameraAvailable = true;
                        _isRunning = true;
                        Debug.WriteLine($"Camera device accessible: {devicePath}");

                        // 初始化RGB帧缓冲区
                        _frameBuffer = new byte[_width * _height * 3];

                        // 启动FFmpeg进程进行摄像头捕获
                        StartFFmpegCapture();

                        return true;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    Debug.WriteLine($"No permission to access {devicePath}");
                    return false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error accessing {devicePath}: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in Open: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭摄像头设备
        /// 停止FFmpeg进程并清理资源
        /// </summary>
        public void Close()
        {
            _isRunning = false;
            _cameraAvailable = false;
            _devicePath = null;

            // 终止FFmpeg进程
            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                try
                {
                    _ffmpegProcess.Kill();
                    _ffmpegProcess.WaitForExit(1000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error stopping ffmpeg process: {ex.Message}");
                }
                finally
                {
                    _ffmpegProcess?.Dispose();
                    _ffmpegProcess = null;
                }
            }
        }

        /// <summary>
        /// 异步捕获一帧图像
        /// 根据摄像头可用性返回真实图像或测试图案
        /// </summary>
        /// <returns>捕获的位图，失败时返回null</returns>
        public async Task<WriteableBitmap?> CaptureFrameAsync()
        {
            if (!_isRunning)
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    if (_cameraAvailable && _devicePath != null && _ffmpegProcess != null && !_ffmpegProcess.HasExited)
                    {
                        // 摄像头可用且FFmpeg进程正常，尝试捕获真实帧
                        return CaptureRealFrame();
                    }
                    else if (_cameraAvailable)
                    {
                        // 摄像头可用但FFmpeg失败，显示摄像头测试图案
                        return CreateCameraTestPattern();
                    }
                    else
                    {
                        // 摄像头不可用，显示普通测试图案
                        return CreateTestPattern();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in CaptureFrameAsync: {ex.Message}");
                    return CreateTestPattern();
                }
            });
        }

        /// <summary>
        /// 从指定区域提取RGB颜色值
        /// 如果有真实摄像头数据则从帧缓冲区提取，否则返回模拟值
        /// </summary>
        /// <param name="centerX">中心点X坐标</param>
        /// <param name="centerY">中心点Y坐标</param>
        /// <param name="size">提取区域大小（当前未使用）</param>
        /// <returns>RGB颜色值</returns>
        public (byte R, byte G, byte B) ExtractRgbFromRegion(int centerX, int centerY, int size)
        {
            if (_cameraAvailable && _frameBuffer != null)
            {
                try
                {
                    // 从真实捕获的帧中提取颜色
                    int x = Math.Max(0, Math.Min(_width - 1, centerX));
                    int y = Math.Max(0, Math.Min(_height - 1, centerY));
                    int offset = (y * _width + x) * 3;

                    return (_frameBuffer[offset], _frameBuffer[offset + 1], _frameBuffer[offset + 2]);
                }
                catch
                {
                    // 提取失败时返回备用颜色
                    return ((byte)(centerX % 256), (byte)(centerY % 256), (byte)200);
                }
            }
            else
            {
                // 测试模式颜色
                return ((byte)(centerX % 256), (byte)(centerY % 256), (byte)128);
            }
        }

        /// <summary>
        /// 释放资源
        /// 实现IDisposable接口
        /// </summary>
        public void Dispose()
        {
            Close();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 启动FFmpeg进程进行摄像头捕获
        /// 使用V4L2接口读取摄像头数据并转换为RGB24格式输出到标准输出
        /// </summary>
        private void StartFFmpegCapture()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    // FFmpeg命令参数说明：
                    // -f v4l2: 使用Video4Linux2输入格式
                    // -i {_devicePath}: 输入设备路径
                    // -vf scale={_width}:{_height}: 缩放到指定尺寸
                    // -pix_fmt rgb24: 输出RGB24像素格式
                    // -f rawvideo: 输出原始视频格式
                    // -: 输出到标准输出
                    Arguments = $"-f v4l2 -i {_devicePath} -vf scale={_width}:{_height} -pix_fmt rgb24 -f rawvideo -",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,      // 重定向标准输出以读取图像数据
                    RedirectStandardError = true,       // 重定向标准错误以捕获错误信息
                    CreateNoWindow = true               // 不创建控制台窗口
                };

                _ffmpegProcess = new Process { StartInfo = startInfo };
                _ffmpegProcess.Start();

                Debug.WriteLine("FFmpeg process started for camera capture");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start ffmpeg: {ex.Message}");
                _ffmpegProcess = null;
            }
        }

        /// <summary>
        /// 从FFmpeg进程捕获真实摄像头帧
        /// 从FFmpeg的标准输出读取RGB24格式的原始图像数据
        /// </summary>
        /// <returns>RGB位图，失败时返回测试图案</returns>
        private WriteableBitmap? CaptureRealFrame()
        {
            try
            {
                if (_ffmpegProcess?.StandardOutput.BaseStream == null || _frameBuffer == null)
                    return CreateCameraTestPattern();

                // 从FFmpeg标准输出读取帧数据
                var stream = _ffmpegProcess.StandardOutput.BaseStream;
                int totalBytesRead = 0;
                int frameSize = _width * _height * 3;  // RGB24格式，每像素3字节

                // 循环读取直到获得完整帧
                while (totalBytesRead < frameSize)
                {
                    int bytesRead = stream.Read(_frameBuffer, totalBytesRead, frameSize - totalBytesRead);
                    if (bytesRead == 0)
                    {
                        // 流结束或无数据可用
                        return CreateCameraTestPattern();
                    }
                    totalBytesRead += bytesRead;
                }

                // 从帧缓冲区创建位图
                var bitmap = new WriteableBitmap(
                    new Avalonia.PixelSize(_width, _height),
                    new Avalonia.Vector(96, 96),
                    PixelFormats.Rgb24,
                    AlphaFormat.Opaque);

                using (var buffer = bitmap.Lock())
                {
                    unsafe
                    {
                        var ptr = (byte*)buffer.Address.ToPointer();
                        var stride = buffer.RowBytes;

                        // 将帧缓冲区数据复制到位图
                        for (int y = 0; y < _height; y++)
                        {
                            for (int x = 0; x < _width; x++)
                            {
                                var srcOffset = (y * _width + x) * 3;
                                var dstOffset = y * stride + x * 3;

                                ptr[dstOffset] = _frameBuffer[srcOffset];       // R
                                ptr[dstOffset + 1] = _frameBuffer[srcOffset + 1]; // G
                                ptr[dstOffset + 2] = _frameBuffer[srcOffset + 2]; // B
                            }
                        }
                    }
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error capturing real frame: {ex.Message}");
                return CreateCameraTestPattern();
            }
        }

        /// <summary>
        /// 创建摄像头连接状态的测试图案
        /// 用绿色边框表示摄像头设备已连接但FFmpeg捕获失败
        /// </summary>
        /// <returns>带绿色边框的测试图案位图</returns>
        private WriteableBitmap CreateCameraTestPattern()
        {
            var bitmap = new WriteableBitmap(
                new Avalonia.PixelSize(_width, _height),
                new Avalonia.Vector(96, 96),
                PixelFormats.Rgb24,
                AlphaFormat.Opaque);

            using (var buffer = bitmap.Lock())
            {
                unsafe
                {
                    var ptr = (byte*)buffer.Address.ToPointer();
                    var stride = buffer.RowBytes;

                    var time = DateTime.Now.Millisecond / 4;
                    for (int y = 0; y < _height; y++)
                    {
                        for (int x = 0; x < _width; x++)
                        {
                            var offset = y * stride + x * 3;

                            // 创建显示摄像头已连接的图案
                            // 绿色调表示真实摄像头模式
                            ptr[offset] = (byte)((x + time) % 128);         // R (降低)
                            ptr[offset + 1] = (byte)((y + time) % 256);     // G (完整)
                            ptr[offset + 2] = (byte)((time) % 128);         // B (降低)

                            // 添加边框以区别于普通测试图案
                            if (x < 10 || x > _width - 10 || y < 10 || y > _height - 10)
                            {
                                ptr[offset] = 0;       // R
                                ptr[offset + 1] = 255; // G (绿色边框)
                                ptr[offset + 2] = 0;   // B
                            }
                        }
                    }
                }
            }

            return bitmap;
        }

        /// <summary>
        /// 创建普通测试图案
        /// 当摄像头设备不可用时显示的彩色动态图案
        /// </summary>
        /// <returns>彩色测试图案位图</returns>
        private WriteableBitmap CreateTestPattern()
        {
            var bitmap = new WriteableBitmap(
                new Avalonia.PixelSize(_width, _height),
                new Avalonia.Vector(96, 96),
                PixelFormats.Rgb24,
                AlphaFormat.Opaque);

            using (var buffer = bitmap.Lock())
            {
                unsafe
                {
                    var ptr = (byte*)buffer.Address.ToPointer();
                    var stride = buffer.RowBytes;

                    // 生成基于时间的动态彩色图案
                    var time = DateTime.Now.Millisecond / 4;
                    for (int y = 0; y < _height; y++)
                    {
                        for (int x = 0; x < _width; x++)
                        {
                            var offset = y * stride + x * 3;
                            ptr[offset] = (byte)((x + time) % 256);         // R
                            ptr[offset + 1] = (byte)((y + time) % 256);     // G  
                            ptr[offset + 2] = (byte)(time % 256);           // B
                        }
                    }
                }
            }

            return bitmap;
        }

        #endregion
    }
}