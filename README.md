# PwdTool —— Windows 简易密码管理器

[![Build & Test](https://github.com/DrizzleTang/pwdTool/actions/workflows/build.yml/badge.svg)](https://github.com/DrizzleTang/pwdTool/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

一个轻量级的 Windows 本地密码存储小工具：全局快捷键在鼠标位置弹出账号密码框，
托盘常驻，支持标签分类搜索、自动填写、浏览器导入、开机自启。

## 功能一览

| # | 功能 | 说明 |
|---|------|------|
| 1 | 快捷键弹窗 + 常用账号 | 默认 `Ctrl+Alt+P`，在**鼠标当前位置**弹出矩形框，展示最近/常用的 5 条账号密码 |
| 2 | 搜索框 | 顶部搜索框可按 **标题 / 账号 / 网址 / 标签分类** 实时过滤匹配 |
| 3 | 展示框交互 | 以鼠标位置为左上角展示；点击框外任意位置自动收起；上下键选择、回车确认；账号库为空/无匹配结果时有占位提示 |
| 4 | 浏览器导入 | 读取 Chrome / Edge 已保存的密码（旧版加密可直读，新版需配合 CSV 导入，见下文说明） |
| 5 | 托盘图标 + 设置页 | 系统托盘常驻，双击/右键打开设置 |
| 6 | 设置项 | 展示框大小、背景色/高亮色、是否"主动填写"、开机自启动、全局快捷键（实时显示是否生效） |
| 7 | 自动填写 | 选中账号后模拟输入"账号 + Tab + 密码"到原窗口，注入前二次确认目标窗口仍在前台；失败时自动改为复制密码到剪贴板（30 秒后自动清空） |
| + | 标签分类 | 每条账号可打多个标签（如"工作"、"银行"），搜索框同时匹配标签 |
| + | 主密码（可选） | 默认仅用 Windows DPAPI 加密账号库，设置里可开启"主密码"二次加密；离线校验信息本身也经 DPAPI 保护 |

## 直接下载使用（无需安装 .NET）

`publish/PwdTool-win-x64-v1.0.0.zip` 是**自包含单文件发布**（已内置完整 .NET 8 运行时，
带自定义应用图标），在 Windows 上解压后双击 `PwdTool.exe` 即可运行，无需另外安装任何运行时。

压缩包内附 `使用说明.txt`（中文使用指南）。首次运行如果被 SmartScreen 拦截
（因为没有购买代码签名证书，这是任何未签名新程序都会遇到的正常提示），
选择"更多信息 → 仍要运行"即可。

### 关于一键安装程序（.exe Setup）

`installer/PwdTool.nsi` 是用 [NSIS](https://nsis.sourceforge.io/) 编写的完整安装脚本
（开始菜单快捷方式、卸载条目、可选开机自启、自定义图标、`DisplayIcon`）。本项目在
当前开发所用的 Linux 沙箱环境里交叉编译好了可执行程序，但沙箱自带的 NSIS 3.09 版本
在编译"包含安装向导页面 + 我们这个体积的自包含单文件"时会稳定触发其自身的内部崩溃
（与文件路径、文件名、压缩器种类都无关，定位后判断是这份 NSIS 二进制自身的问题，
非本项目代码问题）。因此本次交付以**解压即用的 zip 包**为准；如果你有一台正常的
Windows 机器（或其它未受此问题影响的 NSIS 环境），可以直接用以下命令生成正式的
`PwdTool-Setup-1.0.0.exe` 一键安装程序：

```powershell
# 需要先安装 NSIS (https://nsis.sourceforge.io/Download)
cd installer
makensis -DAPP_VERSION=1.0.0 -DPUBLISH_DIR=..\publish\win-x64 -DOUT_FILE=..\publish\PwdTool-Setup-1.0.0.exe PwdTool.nsi
```

## 从源码构建

项目为标准 .NET 8 WinForms 项目，**Windows / macOS / Linux 均可编译**（面向 Windows 的
引用程序集通过 NuGet 跨平台还原，无需在 Windows 上才能编译；本仓库已在 Linux 环境下
用 `dotnet build` 验证通过，0 警告 0 错误，且把可空性警告提升为编译错误以防回归）。

```bash
cd src
dotnet build -c Release                     # 编译验证
dotnet run                                   # 直接运行（仅限 Windows）

# 生成免安装单文件可执行程序（Windows x64）：
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o ../publish/win-x64
```

### 运行测试

```bash
dotnet test tests/PwdTool.Tests/PwdTool.Tests.csproj -c Release
```

测试项目引用了启用 WinForms 的主项目，其输出依赖仅限 Windows 的
`Microsoft.WindowsDesktop.App` 共享运行时，因此 `dotnet test` **必须在 Windows 上执行**
（本仓库开发用的 Linux 沙箱里，测试宿主进程根本无法启动，不止是个别用例被跳过）。
仓库自带的 GitHub Actions（`.github/workflows/build.yml`）在 `windows-latest` 上跑
build + test + publish，每次推送/PR 都会验证。测试覆盖：账号排序权重
（`AccountEntryTests`）、CSV 导入解析（`BrowserImporterCsvTests`，纯逻辑，在任何平台都
能跑）、账号库加解密往返与主密码流程（`PasswordStoreTests`，需要真实 Windows DPAPI）。

## 使用指南

1. 运行后程序常驻**系统托盘**（无主窗口），双击托盘图标打开设置。
2. 按下全局快捷键（默认 `Ctrl+Alt+P`）：在**当前鼠标位置**弹出搜索框 + 最近常用
   账号列表（默认 5 条）。输入关键字按标题/账号/网址/标签过滤；`↑`/`↓` 选择，
   `Enter` 确认，`Esc` 或点击框外关闭；账号库为空或搜索无结果时会显示占位提示。
3. 选中账号后：
   - 若开启"主动填写"（默认开启）：尝试把"账号 + Tab + 密码"输入到刚才操作的窗口，
     注入前会二次确认该窗口仍在前台（避免焦点被其它系统事件抢走导致密码打错地方）；
   - 若目标窗口拒绝注入（例如以管理员权限运行的程序）或关闭了该选项：改为把密码
     复制到剪贴板（30 秒后若剪贴板内容未变会自动清空），并有托盘气泡提示。
4. 右键托盘图标：显示密码框 / 打开设置 / 切换开机自启 / 退出。
5. 设置窗口分四个页签：
   - **账号管理**：新增/编辑/删除账号（含标签），从 Chrome/Edge 导入（后台线程执行，
     不阻塞界面），或从 CSV 导入；
   - **展示框外观**：宽高、背景色、选中高亮色（选中项文字颜色会按背景亮度自动选黑/白，
     保证任意自定义强调色下都可读）；
   - **常规**：是否主动填写、开机自启（状态以注册表实际值为准）、全局快捷键
     （点击快捷键框后直接按下新组合，旁边实时显示"当前是否生效"）；
   - **安全**：开启/关闭/修改主密码。

## 安全与合规说明（请务必阅读）

### 本地存储加密

- 默认仅用 **Windows DPAPI**（`CurrentUser` 范围）加密账号库文件
  `%AppData%\PwdTool\vault.dat`，与当前 Windows 账户绑定，重启后免密自动解锁；
  写入采用"先写临时文件再原子替换"，并保留一份 `.bak` 备份，降低崩溃/断电导致
  数据损坏的风险。局限：同一账户下运行的其它程序理论上也能用 DPAPI 解密该文件。
- 设置里可选开启"**主密码**"：在 DPAPI 之外，再用 PBKDF2-SHA256（迭代 210,000）
  派生密钥、AES-256-GCM 加密账号库。开启/关闭/修改主密码均采用"先落盘成功、
  再提交内存状态"的事务式写法，磁盘写入失败不会导致内存状态与磁盘内容不一致。
  开启后每次启动需要输入主密码，安全性更高，但**丢失主密码将无法恢复账号库**，
  请务必牢记。
- `settings.json`（展示框大小/颜色、是否自动填写、开机自启、快捷键等）本身是明文
  JSON，但其中的主密码"离线校验信息"（用于设置页面快速校验旧密码，而非解锁账号库
  本身）会先打包再整体经 DPAPI 加密后才写入，不会明文暴露——避免攻击者仅凭这个
  文件就能离线暴力破解主密码，绕开 DPAPI"必须同一 Windows 账户"这层防护。

### 浏览器导入的真实限制（App-Bound Encryption）

Chrome / Edge 自 127 版起（2024-07，到现在已默认全量启用）引入了
**App-Bound Encryption（ABE）**：密码改用 `v20` 格式加密，密钥受 SYSTEM 级
Elevation 服务二次包裹，**设计目的就是阻止普通用户态第三方程序直接解密**，
这是浏览器官方的安全加固，不是本程序的缺陷。因此：

- 本程序**可以**直接解密仍是旧版 `v10`/`v11` 格式的密码（未迁移的旧账号，
  或更早版本 Chrome/Edge 保存的密码），并会一并读取 `-wal`/`-shm` 伴随文件，
  避免浏览器仍在运行时读不到最近写入的密码；
- 对新版 `v20` 密码，本程序**不会**尝试注入浏览器进程或提权破解（那等同于
  信息窃取器技术，会被杀毒软件拦截，且随浏览器版本更新随时失效，不适合作为
  一个正当工具的实现方式）；
- 遇到 `v20` 密码会在导入结果里明确提示跳过数量，请改用浏览器"设置 → 密码 →
  导出密码"生成 CSV 文件，再用设置里的"从 CSV 导入"补全。

### 自动填写的限制

`SendInput` 受 Windows UIPI（用户界面权限隔离）管制，**无法**向以管理员权限运行、
完整性级别更高的窗口注入按键（且这种失败是静默的），也无法作用于 UAC 安全桌面、
锁屏/登录界面。遇到这些情况会自动降级为复制密码到剪贴板。注入前还会二次确认
目标窗口确实仍在前台，避免这段等待期间焦点被其它系统事件抢走导致密码被打进
错误的窗口。

## 项目结构

```
src/
  PwdTool.csproj          项目文件 (net8.0-windows, WinForms)
  app.manifest            PerMonitorV2 DPI 感知 + asInvoker 权限声明
  Program.cs              入口：单实例 Mutex + 全局异常兜底 + 启动托盘上下文
  TrayAppContext.cs        托盘常驻上下文：图标/菜单/热键/弹窗生命周期（幂等清理）
  Assets/app.ico           自定义应用图标（托盘/设置窗口/安装程序共用）
  Models/AccountEntry.cs   账号模型（含标签、排序权重、Clone、ToString）
  Services/
    AppSettings.cs         非敏感设置的 JSON 持久化（原子写入，损坏时明确提示而非静默重置）
    PasswordStore.cs       账号库 CRUD + DPAPI/主密码两层加密（事务式写入、原子落盘）
    HotkeyManager.cs       全局热键 (RegisterHotKey) 封装，暴露注册状态与抑制开关
    AutoTypeService.cs     SendInput 自动填写，含前台窗口二次校验
    AutoStartManager.cs    开机自启（注册表 Run 项）
    BrowserImporter.cs     Chrome/Edge 直读（含 WAL/SHM）+ CSV 导入
    ClipboardHelper.cs     密码复制到剪贴板 + 自动清除
    AppIconProvider.cs     从可执行文件自身提取嵌入图标
    JsonContext.cs         System.Text.Json 源生成序列化上下文
    Native.cs              集中的 P/Invoke 声明
  Forms/
    PopupForm.cs           鼠标位置弹窗（搜索 + 列表 + 空态提示）
    SettingsForm.cs        设置窗口（四个页签，异步导入，热键实时状态）
    EditEntryForm.cs        新增/编辑账号（含标签，编辑前会克隆避免"取消"产生副作用）
    MasterPasswordForm.cs   主密码 解锁/设置/修改 对话框（AutoSize 布局）
tests/
  PwdTool.Tests/           xUnit 测试项目（见"运行测试"）
installer/
  PwdTool.nsi              NSIS 一键安装脚本（含自定义图标、taskkill 失败提示）
publish/
  win-x64/                 dotnet publish 产出的自包含单文件
  PwdTool-win-x64-v1.0.0.zip  解压即用的分发包
.github/workflows/build.yml  CI：windows-latest 上 build + test + publish
LICENSE                    MIT 许可证
```

## 自测清单

以下功能已在本仓库通过 `dotnet build -c Release`（0 警告 0 错误）与
`dotnet publish -r win-x64 --self-contained` 验证代码可正确编译、发布产物可生成，
纯逻辑单元测试（标签解析/排序权重/CSV 解析）也已通过人工核对验证正确性；
由于开发环境是 Linux 沙箱、无法运行 WinForms 界面与需要 DPAPI 的用例，
请在 Windows 上按以下清单逐项手动验证（或直接跑 `dotnet test`，见上文）：

- [ ] 双击 `PwdTool.exe`，任务栏出现托盘图标（自定义锁形图标），无多余窗口
- [ ] 按 `Ctrl+Alt+P`，弹窗出现在鼠标位置左上角，搜索框自动获得焦点
- [ ] 输入关键字，列表按标题/账号/网址/标签实时过滤；清空账号库后弹窗显示"暂无账号"提示
- [ ] 点击弹窗外部，弹窗自动消失；再次按快捷键，弹窗重新出现（toggle）
- [ ] 选中一条账号回车，账号+密码被填写到之前聚焦的输入框（或复制到剪贴板+气泡提示，
      30 秒后剪贴板内容若未变会自动清空）
- [ ] 设置 → 账号管理：新增/编辑/删除账号，标签能被搜索框搜到；编辑到一半点"取消"不影响原数据
- [ ] 设置 → 展示框外观：调整宽高/颜色后弹窗立即生效；选中浅色强调色时选中项文字仍清晰可读
- [ ] 设置 → 常规：录制新快捷键（旁边实时显示"当前已生效/未生效"）、切换主动填写、
      切换开机自启（重启后仍生效，且与"任务管理器 > 启动"里的实际状态一致）
- [ ] 设置 → 安全：开启主密码后重启程序需要输入主密码；关闭主密码若中途失败会有错误提示
      而不是静默异常；忘记密码场景已在 UI 提示
- [ ] 设置 → 账号管理：从 Chrome/Edge 导入（导入期间按钮禁用、显示等待光标），能看到导入
      结果与"新版密码已跳过"的数量提示
- [ ] 通过 NSIS 安装程序勾选"开机自启动"选项安装后，首次运行设置页的开机自启勾选状态
      与实际一致
- [ ] 卸载/删除后 `%AppData%\PwdTool\` 下的 `vault.dat`/`settings.json`（含 `.tmp`/`.bak`）
      清理正常

## 已知限制

- 未做代码签名，首次运行会被 SmartScreen/杀毒软件提示"未知发布者"。
- 浏览器导入仅覆盖旧版 `v10`/`v11` 密码，新版 `v20` 需要 CSV 导入补全（见上文说明）。
- 自动填写无法作用于以管理员权限运行的窗口（Windows UIPI 限制）。
- 主密码为可选功能，一旦忘记将无法恢复账号库数据。
- `PublishTrimmed`/`PublishReadyToRun` 发布体积/启动优化已评估但未启用——项目已改用
  System.Text.Json 源生成上下文降低裁剪风险，但因开发环境（Linux 沙箱）无法在真实
  Windows 上验证裁剪后功能完好，出于稳妥考虑留给有 Windows 测试条件的使用者自行开启
  （见 `src/PwdTool.csproj` 注释）。
- 多屏幕不同 DPI 缩放比例下弹窗定位的边界情况尚未在真实多屏环境实测确认，理论上
  PerMonitorV2 声明下应该是准确的。
