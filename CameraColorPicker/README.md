# 摄像头颜色提取器

一个为树莓派5 (ARM64) 开发的跨平台摄像头应用程序，可以从USB摄像头捕获视频并从中心区域提取RGB颜色值。

## 功能特性

- 使用V4L2进行实时摄像头捕获
- 从中心区域提取RGB颜色值
- 带有指向RGB数值的可视化指示线
- 开机自动启动
- 针对树莓派5 ARM64优化

## 系统要求

- 树莓派5 (ARM64)
- USB摄像头
- 7英寸显示屏
- 树莓派操作系统 (64位)

## 构建说明 (Windows环境)

1. 安装 .NET 8 SDK
2. 在项目目录中打开PowerShell
3. 运行构建脚本：
   ```powershell
   .\build-package.ps1
   ```

## 安装说明 (树莓派)

1. 将 `camera-color-picker-deploy.tar.gz` 复制到树莓派
2. 停止旧服务
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
5. 运行安装脚本：
   ```bash
   sudo bash install.sh
   ```

## 使用说明

应用程序将在开机时自动启动。您也可以手动控制它：

### 系统服务控制

```bash
# 启动应用程序
sudo systemctl start camera-color-picker

# 停止应用程序
sudo systemctl stop camera-color-picker

# 检查状态
sudo systemctl status camera-color-picker

# 查看日志
sudo journalctl -u camera-color-picker -f
```

### 手动运行程序

如果需要手动运行程序，请执行以下命令：

```bash
# 进入程序目录
cd /opt/camera-color-picker

# 设置显示环境变量
export DISPLAY=:0
export XDG_RUNTIME_DIR=/run/user/1000

# 运行程序
./CameraColorPicker
```

## 卸载说明

```bash
sudo ./uninstall.sh
```

## 技术详情

- 框架：.NET 8 与 Avalonia UI
- 图像处理：FFmpeg + 自定义V4L2实现
- 摄像头接口：V4L2
- 服务管理：systemd 