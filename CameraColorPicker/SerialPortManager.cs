#nullable enable

using System;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace CameraColorPicker
{
    /// <summary>
    /// 串口通信管理器
    /// 负责通过串口将RGB值和灰度值发送到PC端
    /// </summary>
    public class SerialPortManager : IDisposable
    {
        #region 私有字段

        /// <summary>
        /// 串口通信实例
        /// </summary>
        private SerialPort? _serialPort;

        /// <summary>
        /// 串口是否已连接
        /// </summary>
        private bool _isConnected = false;

        /// <summary>
        /// 发送数据的取消令牌源
        /// </summary>
        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// 线程锁，确保串口操作的线程安全
        /// </summary>
        private readonly object _serialLock = new object();

        #endregion

        #region 公共事件

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<bool>? ConnectionStatusChanged;

        /// <summary>
        /// 数据发送事件
        /// </summary>
        public event EventHandler<string>? DataSent;

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数
        /// </summary>
        public SerialPortManager()
        {
            _cancellationTokenSource = new CancellationTokenSource();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 打开串口连接
        /// </summary>
        /// <param name="portName">串口名称（如/dev/ttyUSB0, /dev/ttyACM0等）</param>
        /// <param name="baudRate">波特率，默认9600</param>
        /// <returns>是否连接成功</returns>
        public bool OpenPort(string portName, int baudRate = 9600)
        {
            try
            {
                lock (_serialLock)
                {
                    // 如果已经连接，先关闭
                    if (_isConnected)
                    {
                        ClosePort();
                    }

                    // 创建串口实例
                    _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                    {
                        ReadTimeout = 1000,
                        WriteTimeout = 1000,
                        Encoding = Encoding.UTF8
                    };

                    // 打开串口
                    _serialPort.Open();
                    _isConnected = true;

                    Debug.WriteLine($"串口连接成功: {portName}, 波特率: {baudRate}");

                    // 触发连接状态变化事件
                    ConnectionStatusChanged?.Invoke(this, true);

                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"串口连接失败: {ex.Message}");
                _isConnected = false;
                ConnectionStatusChanged?.Invoke(this, false);
                return false;
            }
        }

        /// <summary>
        /// 关闭串口连接
        /// </summary>
        public void ClosePort()
        {
            try
            {
                lock (_serialLock)
                {
                    if (_serialPort?.IsOpen == true)
                    {
                        _serialPort.Close();
                    }
                    _serialPort?.Dispose();
                    _serialPort = null;
                    _isConnected = false;

                    Debug.WriteLine("串口连接已关闭");

                    // 触发连接状态变化事件
                    ConnectionStatusChanged?.Invoke(this, false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"关闭串口时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送RGB和灰度值数据
        /// </summary>
        /// <param name="r">红色分量</param>
        /// <param name="g">绿色分量</param>
        /// <param name="b">蓝色分量</param>
        /// <param name="grayscale">灰度值</param>
        /// <returns>是否发送成功</returns>
        public bool SendColorData(byte r, byte g, byte b, byte grayscale)
        {
            try
            {
                lock (_serialLock)
                {
                    if (!_isConnected || _serialPort?.IsOpen != true)
                    {
                        return false;
                    }

                    // 构建数据包格式：RGB:r,g,b;GRAY:grayscale\n
                    var dataString = $"RGB:{r},{g},{b};GRAY:{grayscale}\n";

                    // 发送数据
                    _serialPort.Write(dataString);

                    Debug.WriteLine($"发送数据: {dataString.Trim()}");

                    // 触发数据发送事件
                    DataSent?.Invoke(this, dataString.Trim());

                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"发送数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 异步发送彩色数据（带重试机制）
        /// </summary>
        /// <param name="r">红色分量</param>
        /// <param name="g">绿色分量</param>
        /// <param name="b">蓝色分量</param>
        /// <param name="grayscale">灰度值</param>
        /// <param name="retryCount">重试次数</param>
        /// <returns>是否发送成功</returns>
        public async Task<bool> SendColorDataAsync(byte r, byte g, byte b, byte grayscale, int retryCount = 3)
        {
            for (int i = 0; i < retryCount; i++)
            {
                if (SendColorData(r, g, b, grayscale))
                {
                    return true;
                }

                if (i < retryCount - 1)
                {
                    await Task.Delay(100); // 重试前等待100ms
                }
            }

            return false;
        }

        /// <summary>
        /// 检查串口连接状态
        /// </summary>
        /// <returns>是否已连接</returns>
        public bool IsConnected()
        {
            lock (_serialLock)
            {
                return _isConnected && _serialPort?.IsOpen == true;
            }
        }

        /// <summary>
        /// 获取可用的串口列表
        /// </summary>
        /// <returns>可用串口名称数组</returns>
        public static string[] GetAvailablePorts()
        {
            try
            {
                return SerialPort.GetPortNames();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取串口列表失败: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            ClosePort();
            _cancellationTokenSource?.Dispose();
        }

        #endregion
    }
}