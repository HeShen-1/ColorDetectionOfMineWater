

# 摄像头颜色提取器

一个为树莓派5 (ARM64) 开发的跨平台摄像头应用程序，可以从USB摄像头捕获视频并从中心区域提取RGB颜色值。

---

## 功能特性

- 使用V4L2进行实时摄像头捕获
- 从中心区域提取RGB颜色值
- 带有指向RGB数值的可视化指示线
- **支持鼠标、触摸屏、键盘多种交互方式**
- **串口通信：实时发送RGB与灰度值到PC**
- **RGB显示框智能定位，始终可见**
- 开机自动启动
- 针对树莓派5 ARM64优化

---

## 交互控制说明

### 1. 鼠标与触摸屏控制

- **点击移动**：在摄像头图像上点击任意位置，黄色检测框立即移动
- **拖拽调整**：点击检测框附近区域并拖拽
- **智能光标**：靠近检测框变为手型，其他区域为十字
- **触摸屏**：支持单指点击、拖拽，实时响应
- **边界保护**：检测框始终在图像范围内，100x100像素
- **实时反馈**：移动检测框时，RGB与灰度值立即更新并通过串口发送

### 2. 键盘控制

- **空格键/C键**：重置检测框到图像中心
- **方向键**：每次10像素精确移动检测框

---

## 串口通信功能

- 自动检测并连接常见串口设备（如 /dev/ttyUSB0, /dev/ttyACM0, /dev/ttyAMA0, /dev/serial0, /dev/ttyS0）
- 实时发送格式：`RGB:255,128,64;GRAY:145`
- 波特率：9600，8N1
- 支持Windows（COM口）与Linux（tty设备）
- 串口连接状态、错误处理、重试机制
- 详细配置与故障排查见下方“技术支持”

---

## RGB显示框智能位置调整

为避免检测框靠近窗口边缘时RGB显示框被遮挡，系统实现了智能定位算法：

- **优先级策略**：右上方→左上方→右下方→左下方→水平自适应
- **动态指示线**：自动连接检测框与RGB显示框
- **边界保护**：RGB显示框始终完全在可视区域内
- **兼容所有交互方式**：鼠标、触摸、键盘移动检测框时，RGB显示框自动调整
- **视觉效果**：切换平滑，指示线清晰，避免遮挡重要图像内容

---

## 系统要求

- 树莓派5 (ARM64)
- USB摄像头
- 7英寸显示屏
- 树莓派操作系统 (64位)

---

## 构建与安装

### Windows环境构建

1. 安装 .NET 8 RunTime `dotnet-runtime-8.0.18-linux-arm64.tar.gz`
   - [安装包地址](https://dotnet.microsoft.com/zh-cn/download/dotnet/thank-you/runtime-8.0.18-linux-arm64-binaries)
2. 在项目目录中打开PowerShell
3. 运行构建脚本,会生成部署包 `camera-color-picker-deploy.tar.gz` ：
   ```powershell
   .\build-package.ps1
   ```

### 树莓派安装

1. 将 `camera-color-picker-deploy.tar.gz` 复制到树莓派
2. 停止旧服务(如果安装了旧版本)
   ```bash
   sudo systemctl stop camera-color-picker
   ```
3. 删除旧版本
   ```bash
   sudo rm -rf /opt/camera-color-picker
   ```
4. 解压文件包：
   ```bash
   tar -xzf camera-color-picker-deploy.tar.gz -C camera/
   ```
5. 进入 `camera` 文件夹
   ```bash
   cd camera/
   ```

6. 运行安装脚本：
   ```bash
   sudo bash install.sh
   ```

---

## 使用说明

### 系统服务控制

```bash
sudo systemctl start camera-color-picker
sudo systemctl stop camera-color-picker
sudo systemctl status camera-color-picker
sudo journalctl -u camera-color-picker -f
```

### 手动运行程序

```bash
cd /opt/camera-color-picker
export DISPLAY=:0
export XDG_RUNTIME_DIR=/run/user/1000
./CameraColorPicker
```

---

## 串口通信详细说明

- **数据格式**：`RGB:255,128,64;GRAY:145`（每条以换行结尾）
- **树莓派端配置**：
  - 启用串口（config.txt中enable_uart=1）
  - 用户加入dialout组
  - 常见串口设备：/dev/ttyUSB0, /dev/ttyACM0, /dev/ttyAMA0, /dev/serial0, /dev/ttyS0
- **PC端配置**：
  - Windows：查看COM口，推荐PuTTY、串口调试助手等
  - Linux：推荐minicom、screen、picocom
- **常见问题排查**：
  - 权限不足：加入dialout组或chmod 666
  - 设备被占用：lsof/killall
  - 数据乱码：确认波特率一致
- **高级配置**：可在SerialPortManager.cs中自定义波特率、数据格式、串口设备列表

---

## RGB显示框智能定位技术细节

- **优先级策略**：右上→左上→右下→左下→水平自适应
- **空间检测**：每个候选位置都检测是否完全可见
- **动态指示线**：自动连接检测框与RGB显示框
- **边界保护**：始终在可视区域内
- **兼容性**：支持鼠标、触摸、键盘等所有控制方式
- **常见问题**：窗口太小、缩放比例异常时请调整窗口或重启应用

---

## 测试方法

### 主要测试用例

1. **中心位置**：检测框在中心，RGB显示框应在右上方
2. **边界测试**：将检测框移动到四边和四角，RGB显示框应自动切换到合适位置
3. **窗口缩放**：调整窗口大小，RGB显示框始终可见
4. **连续移动**：拖拽检测框，RGB显示框位置平滑切换
5. **串口通信**：PC端能实时收到正确的RGB与灰度数据

### 检查点

- RGB显示框始终可见
- 指示线正确连接
- RGB与灰度值实时更新
- 串口数据正常发送
- UI响应流畅

### 常见问题排查

- RGB显示框位置异常：增大窗口或重启应用
- 指示线不正确：检查坐标转换与边界检测
- RGB值不更新：检查摄像头连接与数据访问

---

## Q&A
> 有任何问题欢迎留言.