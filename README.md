# H5 免登录一键打包工具（可视化 EXE）

已改为 Windows 可视化程序，不使用 BAT。

## 程序功能

- 选择 uni-app 项目目录
- 选择 HBuilderX 安装目录
- 自动扫描 HBuilderX 安装目录
- 选择 H5 输出目录
- 一键检查配置
- 一键开始打包
- 实时显示构建日志
- 打包前可清理旧产物
- 打包成功后可自动打开输出目录

## 路径选择

程序启动不内置默认项目地址，需要在界面手动选择：

- 项目地址：选择 uni-app 项目根目录
- HBuilderX 目录：可手动选择，也可点击“自动扫描 HBuilderX”
- 导出目录：默认自动设置为 `<项目目录>\dist\build\h5`，也可以手动修改

## 自动扫描 HBuilderX

程序启动时会自动扫描 HBuilderX 安装目录；如果未找到或想更换目录，可以点击界面上的：

```text
自动扫描 HBuilderX
```

扫描范围包括：

- 常见盘符根目录，例如 `D:\HBuilderX`
- `Program Files` / `Program Files (x86)`
- 用户桌面、下载、文档、AppData
- 常见工具目录，例如 `tools`、`software`、`dev`
- 环境变量和 PATH
- Windows 注册表卸载信息

## 原理

程序直接调用：

```text
D:\HBuilderX\plugins\node\node.exe
D:\HBuilderX\plugins\uniapp-cli\bin\uniapp-cli.js
```

并设置：

```text
UNI_INPUT_DIR=<项目目录>
UNI_OUTPUT_DIR=<输出目录>
UNI_PLATFORM=h5
NODE_ENV=production
UNI_MINIMIZE=true
```

不会调用 `cli.exe publish`，所以不会触发 DCloud/HBuilderX 登录或手机号绑定流程。

## 构建后位置

编译完成后，EXE 会放到：

```text
dist\H5免登录一键打包工具.exe
```

发布目录只保留这一个 EXE。用户在界面点击“保存配置”时，配置会写入系统 AppData：

```text
%APPDATA%\H5BuildTool\build-h5.config.json
```
