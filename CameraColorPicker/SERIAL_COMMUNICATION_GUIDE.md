# 树莓派摄像头颜色提取器串口通信指南

## 概述

此应用程序已集成串口通信功能，可以将检测到的RGB颜色值和灰度值实时发送到PC端。

## 功能特性

- ✅ 自动检测并连接可用的串口设备
- ✅ 实时发送RGB值和灰度值
- ✅ 串口连接状态显示
- ✅ 错误处理和重试机制
- ✅ 支持多种串口设备类型

## 数据格式

应用程序发送的数据格式为：
```
RGB:255,128,64;GRAY:145
```

其中：
- `RGB:r,g,b` - RGB三个颜色分量值（0-255）
- `GRAY:grayscale` - 灰度值（0-255，使用标准公式：0.299*R + 0.587*G + 0.114*B）
- 每条数据以换行符（\n）结尾

## 树莓派端配置

### 1. 硬件连接

将USB转串口设备连接到树莓派的USB端口。

### 2. 启用串口功能

```bash
# 编辑启动配置文件
sudo nano /boot/firmware/config.txt

# 添加以下行（如果没有的话）
enable_uart=1
dtoverlay=disable-bt  # 如果需要使用GPIO串口，禁用蓝牙

# 重启树莓派
sudo reboot
```

### 3. 设置串口权限

```bash
# 将用户添加到dialout组以获得串口访问权限
sudo usermod -a -G dialout $USER

# 注销并重新登录，或重启系统
```

### 4. 检查可用串口

```bash
# 查看可用的串口设备
ls /dev/tty*

# 查看串口详细信息
dmesg | grep tty

# 常见的串口设备：
# /dev/ttyUSB0    - USB转串口设备
# /dev/ttyACM0    - USB CDC设备
# /dev/ttyAMA0    - 树莓派硬件串口
# /dev/serial0    - 树莓派主串口别名
# /dev/ttyS0      - 标准串口
```

## PC端配置

### 1. 硬件连接

将USB转串口设备的另一端连接到PC的USB端口。

### 2. 确定COM端口

在Windows设备管理器中查看分配的COM端口号（如COM5）。

### 3. 配置串口助手

推荐设置：
- **端口**: COM5（根据实际情况调整）
- **波特率**: 9600
- **数据位**: 8
- **停止位**: 1
- **校验位**: 无

### 4. 常用串口助手软件

- **Windows**: 
  - PuTTY
  - 串口调试助手
  - AccessPort
  - Tera Term

- **Linux**: 
  - minicom
  - screen
  - picocom

## 应用程序使用

### 1. 编译和运行

```bash
# 进入项目目录
cd CameraColorPicker

# 编译项目
dotnet build

# 运行应用程序
dotnet run

# 或者发布后运行
dotnet publish -c Release
./bin/Release/net8.0/linux-arm64/publish/CameraColorPicker
```

### 2. 界面说明

- **摄像头画面**: 显示实时摄像头图像
- **黄色检测框**: 中心100x100像素的颜色检测区域
- **RGB显示**: 显示检测区域的平均RGB值
- **灰度显示**: 显示计算出的灰度值
- **状态栏**: 显示摄像头状态、串口状态和FPS

### 3. 自动串口检测

应用程序会自动尝试连接以下串口设备：
1. `/dev/ttyUSB0` - USB转串口设备
2. `/dev/ttyACM0` - USB CDC设备
3. `/dev/ttyAMA0` - 树莓派硬件串口
4. `/dev/serial0` - 树莓派主串口别名
5. `/dev/ttyS0` - 标准串口

## 测试方法

### 方法1: 使用Python测试脚本

```bash
# 安装pyserial库
pip install pyserial

# 运行测试脚本
python test_serial.py [串口设备]

# 示例
python test_serial.py /dev/ttyUSB0
```

### 方法2: 使用minicom

```bash
# 安装minicom
sudo apt install minicom

# 配置minicom
sudo minicom -s

# 设置串口参数后保存并运行
minicom
```

### 方法3: 使用screen

```bash
# 直接连接串口
screen /dev/ttyUSB0 9600

# 退出：按 Ctrl+A 然后按 K
```

## 故障排除

### 1. 串口连接失败

**症状**: 状态栏显示"Serial: No device found"

**解决方案**:
- 检查USB转串口设备是否正确连接
- 确认设备驱动已正确安装
- 检查串口设备权限：`ls -l /dev/ttyUSB*`
- 确认用户在dialout组中：`groups`

### 2. 权限不足

**症状**: 权限拒绝错误

**解决方案**:
```bash
# 添加用户到dialout组
sudo usermod -a -G dialout $USER

# 或者临时修改权限
sudo chmod 666 /dev/ttyUSB0
```

### 3. 设备被占用

**症状**: "Resource busy" 错误

**解决方案**:
```bash
# 查看哪个进程在使用串口
sudo lsof /dev/ttyUSB0

# 结束占用进程
sudo killall minicom
```

### 4. 数据接收不正常

**症状**: PC端收到乱码或不完整数据

**解决方案**:
- 确认串口参数设置正确（波特率9600，8N1）
- 检查串口线缆质量
- 确认两端使用相同的波特率

## 高级配置

### 修改波特率

如需修改波特率，请在`SerialPortManager.cs`中调整：

```csharp
// 修改默认波特率
_serialManager?.OpenPort(port, 115200) // 改为115200
```

### 修改数据格式

如需自定义数据格式，请在`SerialPortManager.cs`的`SendColorData`方法中修改：

```csharp
// 自定义数据格式
var dataString = $"R:{r},G:{g},B:{b},GRAY:{grayscale}\r\n";
```

### 添加更多串口设备

在`MainWindow.axaml.cs`的`InitializeSerial`方法中添加更多设备路径：

```csharp
string[] commonPorts = {
    "/dev/ttyUSB0",
    "/dev/ttyUSB1",     // 添加更多USB设备
    "/dev/ttyACM0",
    "/dev/ttyACM1",     // 添加更多CDC设备
    // ... 其他设备
};
```

## 技术支持

如遇到问题，请检查：

1. **硬件连接**: 确保所有线缆连接牢固
2. **设备权限**: 确保有足够权限访问串口设备
3. **软件配置**: 确认串口参数设置正确
4. **系统日志**: 查看系统日志了解详细错误信息

```bash
# 查看系统日志
journalctl -f

# 查看USB设备信息
lsusb

# 查看串口设备信息
setserial -g /dev/ttyUSB0
``` 