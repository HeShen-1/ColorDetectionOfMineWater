#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Diagnostics;

namespace CameraColorPicker
{
    /// <summary>
    /// V4L2摄像头真实实现类
    /// 使用Linux V4L2 API直接与摄像头设备交互，处理YUYV格式并转换为RGB
    /// 相比V4L2Camera类，此类包含更完整的格式转换功能
    /// </summary>
    public class V4L2CameraReal : IDisposable
    {
        #region 私有字段

        /// <summary>
        /// 摄像头设备文件描述符
        /// </summary>
        private int _fd = -1;

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
        /// 原始帧数据缓冲区（YUYV格式）
        /// </summary>
        private byte[] _frameBuffer;

        #endregion

        #region Linux系统调用声明

        /// <summary>
        /// 打开文件系统调用
        /// </summary>
        /// <param name="pathname">文件路径</param>
        /// <param name="flags">打开标志</param>
        /// <returns>文件描述符</returns>
        [DllImport("libc", SetLastError = true)]
        private static extern int open(string pathname, int flags);

        /// <summary>
        /// 关闭文件系统调用
        /// </summary>
        /// <param name="fd">文件描述符</param>
        /// <returns>操作结果</returns>
        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        /// <summary>
        /// 读取文件系统调用
        /// </summary>
        /// <param name="fd">文件描述符</param>
        /// <param name="buf">缓冲区</param>
        /// <param name="count">读取字节数</param>
        /// <returns>实际读取字节数</returns>
        [DllImport("libc", SetLastError = true)]
        private static extern int read(int fd, byte[] buf, int count);

        /// <summary>
        /// 设备控制系统调用（V4L2格式设置）
        /// </summary>
        /// <param name="fd">文件描述符</param>
        /// <param name="request">控制请求码</param>
        /// <param name="fmt">格式结构体</param>
        /// <returns>操作结果</returns>
        [DllImport("libc", SetLastError = true)]
        private static extern int ioctl(int fd, uint request, ref V4L2Format fmt);

        /// <summary>
        /// 设备控制系统调用（流控制）
        /// </summary>
        /// <param name="fd">文件描述符</param>
        /// <param name="request">控制请求码</param>
        /// <param name="type">缓冲区类型</param>
        /// <returns>操作结果</returns>
        [DllImport("libc", SetLastError = true)]
        private static extern int ioctl(int fd, uint request, ref int type);

        #endregion

        #region V4L2常量定义

        /// <summary>
        /// 读写模式打开文件
        /// </summary>
        private const int O_RDWR = 2;

        /// <summary>
        /// V4L2设置格式控制码
        /// </summary>
        private const uint VIDIOC_S_FMT = 0xc0d05605;

        /// <summary>
        /// V4L2开始流控制码
        /// </summary>
        private const uint VIDIOC_STREAMON = 0x40045612;

        /// <summary>
        /// V4L2停止流控制码
        /// </summary>
        private const uint VIDIOC_STREAMOFF = 0x40045613;

        /// <summary>
        /// YUYV像素格式（4:2:2采样）
        /// </summary>
        private const uint V4L2_PIX_FMT_YUYV = 0x56595559;

        /// <summary>
        /// 视频捕获缓冲区类型
        /// </summary>
        private const int V4L2_BUF_TYPE_VIDEO_CAPTURE = 1;

        #endregion

        #region V4L2结构体定义

        /// <summary>
        /// V4L2像素格式结构体
        /// 定义图像的宽高、像素格式等参数
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct V4L2PixFormat
        {
            public uint width;          // 图像宽度
            public uint height;         // 图像高度
            public uint pixelformat;    // 像素格式
            public uint field;          // 场类型
            public uint bytesperline;   // 每行字节数
            public uint sizeimage;      // 图像大小
            public uint colorspace;     // 色彩空间
            public uint priv;           // 私有数据
        }

        /// <summary>
        /// V4L2格式结构体
        /// 包含缓冲区类型和像素格式信息
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct V4L2Format
        {
            public uint type;           // 缓冲区类型
            public V4L2PixFormat fmt;   // 像素格式
        }

        #endregion

        /// <summary>
        /// 构造函数
        /// 初始化YUYV格式的帧缓冲区
        /// </summary>
        public V4L2CameraReal()
        {
            // YUYV格式每个像素占2字节
            _frameBuffer = new byte[_width * _height * 2];
        }

        #region 公共方法

        /// <summary>
        /// 打开摄像头设备
        /// 设置YUYV格式并启动视频流
        /// </summary>
        /// <param name="devicePath">设备路径，默认为/dev/video0</param>
        /// <returns>是否成功打开</returns>
        public bool Open(string devicePath = "/dev/video0")
        {
            try
            {
                // 以读写模式打开设备文件
                _fd = open(devicePath, O_RDWR);
                if (_fd < 0)
                {
                    Debug.WriteLine($"Failed to open {devicePath}");
                    return false;
                }

                // 设置图像格式为YUYV
                var fmt = new V4L2Format
                {
                    type = V4L2_BUF_TYPE_VIDEO_CAPTURE,
                    fmt = new V4L2PixFormat
                    {
                        width = (uint)_width,
                        height = (uint)_height,
                        pixelformat = V4L2_PIX_FMT_YUYV,
                        field = 1
                    }
                };

                if (ioctl(_fd, VIDIOC_S_FMT, ref fmt) < 0)
                {
                    Debug.WriteLine("Failed to set format");
                    Close();
                    return false;
                }

                // 启动视频流
                int type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
                if (ioctl(_fd, VIDIOC_STREAMON, ref type) < 0)
                {
                    Debug.WriteLine("Failed to start streaming");
                    Close();
                    return false;
                }

                _isRunning = true;
                Debug.WriteLine($"Camera opened successfully: {devicePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception opening camera: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 关闭摄像头设备
        /// 停止视频流并关闭设备文件
        /// </summary>
        public void Close()
        {
            if (_fd >= 0)
            {
                try
                {
                    // 停止视频流
                    int type = V4L2_BUF_TYPE_VIDEO_CAPTURE;
                    ioctl(_fd, VIDIOC_STREAMOFF, ref type);

                    // 关闭设备文件
                    close(_fd);
                }
                catch { }
                _fd = -1;
            }
            _isRunning = false;
        }

        /// <summary>
        /// 异步捕获一帧图像
        /// 从摄像头读取YUYV数据并转换为RGB位图
        /// </summary>
        /// <returns>RGB格式的位图，失败时返回测试图案</returns>
        public async Task<WriteableBitmap?> CaptureFrameAsync()
        {
            if (_fd < 0 || !_isRunning)
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    // 尝试从摄像头读取帧数据
                    int bytesRead = read(_fd, _frameBuffer, _frameBuffer.Length);
                    if (bytesRead <= 0)
                    {
                        Debug.WriteLine("Failed to read frame data");
                        return CreateTestPattern();
                    }

                    // 将YUYV格式转换为RGB
                    return ConvertYUYVToRGB(_frameBuffer, bytesRead);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error capturing frame: {ex.Message}");
                    return CreateTestPattern();
                }
            });
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 将YUYV格式数据转换为RGB位图
        /// YUYV是4:2:2采样格式，每2个像素共享UV分量
        /// </summary>
        /// <param name="yuvData">YUYV格式原始数据</param>
        /// <param name="dataSize">数据大小</param>
        /// <returns>RGB格式位图</returns>
        private WriteableBitmap ConvertYUYVToRGB(byte[] yuvData, int dataSize)
        {
            try
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
                        var rgbPtr = (byte*)buffer.Address.ToPointer();
                        var stride = buffer.RowBytes;

                        // 转换YUYV到RGB
                        for (int y = 0; y < _height; y++)
                        {
                            for (int x = 0; x < _width; x += 2)
                            {
                                // 检查数据边界
                                if ((y * _width + x) * 2 + 3 >= dataSize) break;

                                // YUYV格式：Y1 U Y2 V（每4字节表示2个像素）
                                int yuvIndex = (y * _width + x) * 2;
                                int y1 = yuvData[yuvIndex];     // 第一个像素的Y（亮度）
                                int u = yuvData[yuvIndex + 1];  // 共享的U（色度）
                                int y2 = yuvData[yuvIndex + 2]; // 第二个像素的Y（亮度）
                                int v = yuvData[yuvIndex + 3];  // 共享的V（色度）

                                // 转换第一个像素的YUV到RGB
                                var rgb1 = YUVToRGB(y1, u, v);
                                int rgbIndex1 = y * stride + x * 3;
                                if (rgbIndex1 + 2 < stride * _height)
                                {
                                    rgbPtr[rgbIndex1] = rgb1.r;
                                    rgbPtr[rgbIndex1 + 1] = rgb1.g;
                                    rgbPtr[rgbIndex1 + 2] = rgb1.b;
                                }

                                // 转换第二个像素的YUV到RGB
                                if (x + 1 < _width)
                                {
                                    var rgb2 = YUVToRGB(y2, u, v);
                                    int rgbIndex2 = y * stride + (x + 1) * 3;
                                    if (rgbIndex2 + 2 < stride * _height)
                                    {
                                        rgbPtr[rgbIndex2] = rgb2.r;
                                        rgbPtr[rgbIndex2 + 1] = rgb2.g;
                                        rgbPtr[rgbIndex2 + 2] = rgb2.b;
                                    }
                                }
                            }
                        }
                    }
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error converting YUYV to RGB: {ex.Message}");
                return CreateTestPattern();
            }
        }

        /// <summary>
        /// YUV颜色空间到RGB颜色空间的转换
        /// 使用ITU-R BT.601标准的转换矩阵
        /// </summary>
        /// <param name="y">Y分量（亮度）</param>
        /// <param name="u">U分量（蓝色色度）</param>
        /// <param name="v">V分量（红色色度）</param>
        /// <returns>RGB颜色值</returns>
        private (byte r, byte g, byte b) YUVToRGB(int y, int u, int v)
        {
            // YUV到RGB的转换公式（ITU-R BT.601）
            int c = y - 16;
            int d = u - 128;
            int e = v - 128;

            // 计算RGB值
            int r = (298 * c + 409 * e + 128) >> 8;
            int g = (298 * c - 100 * d - 208 * e + 128) >> 8;
            int b = (298 * c + 516 * d + 128) >> 8;

            // 确保RGB值在0-255范围内
            return ((byte)Math.Max(0, Math.Min(255, r)),
                    (byte)Math.Max(0, Math.Min(255, g)),
                    (byte)Math.Max(0, Math.Min(255, b)));
        }

        /// <summary>
        /// 创建测试图案
        /// 当摄像头读取失败时显示彩色动态测试图案
        /// </summary>
        /// <returns>测试图案位图</returns>
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

                    // 生成基于时间的动态测试图案
                    var time = DateTime.Now.Millisecond / 4;
                    for (int y = 0; y < _height; y++)
                    {
                        for (int x = 0; x < _width; x++)
                        {
                            var offset = y * stride + x * 3;
                            ptr[offset] = (byte)((x + time) % 256);     // R
                            ptr[offset + 1] = (byte)((y + time) % 256); // G
                            ptr[offset + 2] = (byte)(time % 256);       // B
                        }
                    }
                }
            }

            return bitmap;
        }

        /// <summary>
        /// 从指定区域提取RGB颜色值
        /// 注意：当前实现返回基于坐标的模拟颜色值
        /// </summary>
        /// <param name="centerX">中心点X坐标</param>
        /// <param name="centerY">中心点Y坐标</param>
        /// <param name="size">提取区域大小</param>
        /// <returns>RGB颜色值</returns>
        public (byte R, byte G, byte B) ExtractRgbFromRegion(int centerX, int centerY, int size)
        {
            // 这个方法将基于实际帧数据实现
            return ((byte)(centerX % 256), (byte)(centerY % 256), (byte)128);
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
    }
}