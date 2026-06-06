# hbuilderBuildH5Tool

HBuilderX uni-app H5 免登录本地打包工具。

这是一个 Windows 可视化 EXE 工具，用于把 HBuilderX / uni-app 项目本地编译成 H5 静态资源。工具不会调用 `cli.exe publish`，因此不会触发 DCloud / HBuilderX 登录、手机号绑定、云端发行等流程。

## 适用场景

- 需要把 uni-app 项目打包成 H5 静态资源
- 不想登录 DCloud / HBuilderX 账号
- 不需要 App 云打包、uniCloud 前端网页托管、原生插件云端发行
- 希望通过可视化界面选择项目、HBuilderX、导出目录后“一键打包”

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

## 快速使用

直接双击：

```text
dist\H5免登录一键打包工具.exe
```

然后按顺序操作：

1. 选择 `项目地址`
2. 选择或自动扫描 `HBuilderX 目录`
3. 确认 `导出目录`
4. 点击 `检查配置`
5. 点击 `开始打包`

打包成功后，导出目录中会生成 H5 静态文件，例如：

```text
index.html
static/
assets/
```

## 路径选择

程序启动不内置默认项目地址，需要在界面手动选择：

- 项目地址：选择 uni-app 项目根目录
- HBuilderX 目录：可手动选择，也可点击“自动扫描 HBuilderX”
- 导出目录：默认自动设置为 `<项目目录>\dist\build\h5`，也可以手动修改

## 项目目录要求

选择的项目地址应为 uni-app 项目根目录，至少需要包含：

```text
manifest.json
pages.json
main.js
```

如果缺少这些文件，工具会在“检查配置”阶段提示错误。

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

简化流程：

```text
本工具 EXE
  ↓ 设置 UNI_INPUT_DIR / UNI_OUTPUT_DIR / UNI_PLATFORM 等环境变量
HBuilderX 内置 node.exe
  ↓ 执行
uniapp-cli.js
  ↓ 调用
HBuilderX 内置 uni-app 编译器
  ↓ 输出
H5 静态资源
```

## 构建后位置

编译完成后，EXE 会放到：

```text
dist\H5免登录一键打包工具.exe
```

发布目录只保留这一个 EXE。用户在界面点击“保存配置”时，配置会写入系统 AppData：

```text
%APPDATA%\H5BuildTool\build-h5.config.json
```

## 从源码构建

本项目使用 .NET 6 WinForms。

构建：

```powershell
dotnet build .\src\H5BuildTool\H5BuildTool.csproj -c Release
```

发布单文件 EXE：

```powershell
dotnet publish .\src\H5BuildTool\H5BuildTool.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true /p:DebugType=None /p:DebugSymbols=false -o .\dist
```

## 常见问题

### 1. 这个工具会登录 DCloud 吗？

不会。它不调用 HBuilderX 的 `cli.exe publish` 发行流程，只调用本地 uni-app 编译入口。

### 2. Vue2 / Vue3 都能打包吗？

工具本身不限制 Vue2 或 Vue3。能否打包取决于当前 HBuilderX 版本和项目本身是否支持本地 H5 编译。

通常判断标准是：

> 项目如果能在 HBuilderX 中本地发行 H5，那么一般也能通过本工具打包。

### 3. 为什么导出目录默认是 `dist\build\h5`？

选择项目地址后，工具会自动把导出目录设置为：

```text
<项目目录>\dist\build\h5
```

也可以在界面中手动修改成其它目录。

### 4. 打包失败怎么办？

先点击 `检查配置`，确认：

- 项目目录正确
- HBuilderX 目录正确
- HBuilderX 中存在 `plugins\node\node.exe`
- HBuilderX 中存在 `plugins\uniapp-cli\bin\uniapp-cli.js`
- 导出目录有写入权限

然后查看界面日志中的错误信息。

## 仓库结构

```text
.
├─ dist/
│  └─ H5免登录一键打包工具.exe
├─ src/
│  └─ H5BuildTool/
│     ├─ H5BuildTool.csproj
│     └─ Program.cs
├─ .gitignore
└─ README.md  
```
学 AI 上 [LinuxDo](https://linux.do)
