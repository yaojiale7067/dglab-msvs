# DGLabVSCode for Visual Studio 2026


> 郊狼（DG-Lab）设备会产生电刺激。请从低强度开始，量力而行。使用者需自行承担风险。


## 功能对照

| 原 VSCode 插件 | Visual Studio 2026 插件 |
| --- | --- |
| `DGLabVSCode: 开始连接` 命令 | 菜单 **工具 → DGLab 郊狼 → 开始连接**；编辑器内 `Ctrl+U` |
| 二维码 Webview | **DGLab 二维码** 工具窗口（可复制绑定链接） |
| `DGLabVSCode: 刷新配置` | **刷新配置**（重置强度渐增并重新下发强度/波形） |
| `DGLabVSCode: 设置波形名称` | **设置波形名称**（输入框，写入配置） |
| `dglabvscode.blacklist` | 选项 **黑名单后缀名**（逗号/分号/空格分隔） |
| `dglabvscode.strength` | 选项 **基础强度** |
| `dglabvscode.pulseName` | 选项 **波形名称** |
| `dglabvscode.heartbeatInterval` | 选项 **心跳间隔（秒）** |
| `dglabvscode.maxAddInterval` | 选项 **最大强度渐增** |
| `dglabvscode.onDidStartDebugSession` | 选项 **开始调试时开火** |
| `dglabvscode.onDidTerminateDebugSession` | 选项 **结束调试时开火** |
| `extension.onDidReceiveDebugSessionCustomEvent`（报错惩罚） | 选项 **调试报错时开火** |
| `dglabvscode.onDidChangeBreakpoints` | 选项 **断点变化时开火** |
| `dglabvscode.onDidSaveTextDocument` | 选项 **保存文件时开火** |
| `dglabvscode.condingMaxfFrequency` | 选项 **编辑事件上限（次/20秒）** |
| `dglabvscode.messageSendOption`（仅 A / 仅 B / AB） | 选项 **输出通道** |
| 固定 0.3 / 0.6 / 2 秒的波形时长 | 选项 **输出时长**：编辑代码、调试/断点、保存文件可分别设置；另有 **开火强度保持时长** |
| 编辑代码、保存、调试、断点事件 | 使用 VS 的文本缓冲区、Running Document Table/DTE、DebuggerEvents 重新挂接 |

### 事件实现说明

* **编辑代码**：`IWpfTextViewCreationListener` + `ITextBuffer.Changed`，仅对拥有焦点的编辑器生效；保留“20 秒内超过上限则清空输出队列”的逻辑。
* **保存文件**：`DTE.Events.DocumentEvents.DocumentSaved`。
* **调试开始/结束**：`DebuggerEvents.OnEnterRunMode` / `OnEnterDesignMode`。
* **调试报错**：`DebuggerEvents.OnEnterBreakMode` 的异常中断（`ExceptionThrown` / `ExceptionNotHandled`），对应原插件的 stderr 报错惩罚。
* **断点变化**：Visual Studio 的 EnvDTE 没有公开断点集合变化事件，因此以 1 秒间隔轮询断点签名（数量、位置、启用状态、条件），仅在变化时触发一次。
* **黑名单**：同时检查被保存文件和当前活动文件的扩展名。
* **通道输出**：`AB` / `仅 A` / `仅 B`。
* **一键开火**：清除波形 → 临时提升 A/B 强度 → 发送波形 → 到达 **开火强度保持时长** 后恢复。
* **输出时长**：编辑代码默认 0.3 秒；调试/断点默认 0.6 秒；保存文件默认 2 秒。数值范围 0.05–30 秒。

## 构建

环境要求：

* Visual Studio 2026（18.x），安装 **Visual Studio 扩展开发** 工作负载（VSSDK）。
* .NET Framework 4.7.2 目标包（VS 2026 自带）。
* 首次还原 NuGet 包需要网络。

在仓库根目录执行：

```powershell
.\build.ps1
```

脚本会定位 `vswhere`/MSBuild，还原 `Microsoft.VisualStudio.SDK` 与 `Microsoft.VSSDK.BuildTools`，生成：

```
src\DGLabVS2026\bin\Release\net472\DGLabVS2026.vsix
artifacts\DGLabVS2026.vsix
```

也可以直接用 Visual Studio 打开 `src\DGLabVS2026\DGLabVS2026.csproj` 后生成。

## 安装

1. 关闭 Visual Studio。
2. 双击 `artifacts\DGLabVS2026.vsix`，或运行：
   ```powershell
   & "C:\Program Files\Microsoft Visual Studio\18\...\Common7\IDE\VSIXInstaller.exe" "artifacts\DGLabVS2026.vsix"
   ```
   （实际路径以本机安装为准；本机 VS 2026 为 `E:\vs\Common7\IDE\VSIXInstaller.exe`。）
3. 启动 Visual Studio 2026。

VSIX 的安装目标为 `[18.0, 19.0)`，仅支持 Visual Studio 2026。若需要同时支持 VS 2022，可把 `source.extension.vsixmanifest` 中三个 `InstallationTarget` 的版本下限改为 `17.14`，并用 `Microsoft.VisualStudio.SDK` 17.14 重新编译。

## 使用

1. 打开 **工具 → DGLab 郊狼 → 开始连接**（或在编辑器内按 `Ctrl+U`）。
2. 用郊狼 App 扫描 **DGLab 二维码** 工具窗口中的二维码并确认绑定。
3. 打开 **工具 → 选项 → DGLab → 郊狼插件配置** 调整强度、波形、心跳、事件强度、输出时长、通道、黑名单。
4. 在 **输出时长** 分类中可分别设置编辑代码、调试/断点、保存文件的波形持续时间，以及开火强度保持时长。
5. 修改配置后点 **确定**；随后可执行 **刷新配置** 立即下发强度并清空旧波形。
6. **设置波形名称** 会列出并校验 `wave.json` 中的 16 个波形，保存到配置。

### 快捷键

原插件使用 `Ctrl+U`。Visual Studio 默认把 `Ctrl+U` 绑定到“转换为小写”，仅注册 VSCT 快捷键可能被内置命令抢占，因此本扩展额外用一个 MEF 键盘处理器在编辑器预览阶段拦截 `Ctrl+U`（与原 VSCode 插件 `editorTextFocus` 下的行为一致），保证按 `Ctrl+U` 就是“开始连接”。

如果你希望保留 Visual Studio 的“转换为小写”，可以移除/禁用本扩展的键盘处理器（`Events/DGLabKeyProcessor.cs`），改用菜单命令或 **工具 → 选项 → 环境 → 键盘** 重新绑定 `DGLab.Connect`。

## 目录结构

```
DGLabVS2026.sln
build.ps1
lib/QRCoder.dll                     QRCoder 1.4.3 net40（MIT）
src/DGLabVS2026/
  DGLabPackage.cs                   包入口、命令、DTE 事件
  DGLabPackage.vsct                 菜单/快捷键
  source.extension.vsixmanifest     VSIX 清单（VS 2026）
  Core/DGLabClient.cs               WebSocket 协议、心跳、强度、波形
  Core/DGLabSettings.cs             配置快照/黑名单
  Core/PulseLibrary.cs              wave.json 解析与十六进制编码
  Core/Json.cs                      基于 JavaScriptSerializer 的 JSON 辅助
  Core/ChannelMode.cs               A/B/AB 枚举与中文显示
  Options/DGLabOptionsPage.cs       Tools > Options 配置页
  Events/TextViewCreationListener.cs 文本变化监听（MEF）
  Events/DGLabKeyProcessor.cs       Ctrl+U 拦截（MEF）
  Events/BreakpointWatcher.cs       断点轮询
  ToolWindows/                      二维码工具窗口
  UI/PromptDialog.cs                输入框
  Resources/wave.json               原项目波形库
  Assets/icon.png                   原项目图标
  LICENSE.txt                       GPL-3.0
  THIRD-PARTY-NOTICES.txt           QRCoder MIT 等第三方声明
tests/DGLabSmoke/                   不依赖 VS 的协议/波形/二维码冒烟测试
```

## 冒烟测试

`tests/DGLabSmoke` 是一个 net472 控制台程序，链接插件的协议与波形源码，验证：

* `wave.json` 可解析、16 个波形、默认波形存在；
* 波形十六进制编码长度与内容；
* 可通过 `wss://ws.dungeon-lab.cn/` 拿到 `clientId` 并生成绑定 URL；
* QR 生成结果为合法 PNG。

```powershell
.\build.ps1 -Test
```

该测试会真实连接郊狼服务器并创建一个临时会话（不会绑定设备、不会输出电流）。

## 安全与隐私

* 插件只连接 `wss://ws.dungeon-lab.cn/`，不发送代码内容；只发送事件触发的波形与强度指令。
* `clientId` 会显示在二维码/日志中；拿到该链接的人可以在郊狼 App 中尝试绑定，请勿公开分享。
* 日志写入 `%LOCALAPPDATA%\DGLabVS2026\extension.log`，输出窗口为 **DGLabVSCode**。
* 强度渐增、心跳间隔等参数请谨慎设置。

## 许可证

原项目与本案均采用 **GPL-2.0**
