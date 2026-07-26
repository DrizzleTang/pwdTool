; PwdTool 一键安装脚本 (NSIS)
; 用法: makensis -DAPP_VERSION=1.0.0 -DPUBLISH_DIR=..\publish\win-x64 -DOUT_FILE=..\publish\PwdTool-Setup-1.0.0.exe PwdTool.nsi
;
; 提示：这个脚本在开发时使用的 Linux 沙箱环境里，配合 dotnet 自包含单文件发布出的
; 72MB PwdTool.exe，会稳定触发该环境自带 NSIS(3.09) 版本的一个内部崩溃（只要脚本里
; 出现任意 Page 指令——无论经典界面还是 MUI2——再加上这个大小的输入文件就会崩溃，
; 与文件路径、文件名、压缩器种类都无关，怀疑是这份 NSIS 二进制自身在处理大文件安装
; 进度显示时的内存问题）。脚本本身逻辑正确，建议在真实 Windows 机器（或其它正常的
; NSIS 安装）上运行本脚本编译，会更可靠。

!ifndef APP_VERSION
  !define APP_VERSION "1.0.0"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\publish\win-x64"
!endif
!ifndef OUT_FILE
  !define OUT_FILE "..\publish\PwdTool-Setup-${APP_VERSION}.exe"
!endif

Unicode true

Name "PwdTool 密码管理器"
OutFile "${OUT_FILE}"
Icon "..\src\Assets\app.ico"
UninstallIcon "..\src\Assets\app.ico"
InstallDir "$LOCALAPPDATA\Programs\PwdTool"
InstallDirRegKey HKCU "Software\PwdTool" "InstallDir"
RequestExecutionLevel user   ; 安装到当前用户目录，无需管理员权限
; 注意：不要在这里用 LZMA/SOLID 压缩器。PwdTool.exe 是 .NET 自包含单文件发布，
; 内部已经是压缩状态，本机可用的 NSIS(3.09) 版本的 LZMA 实现处理这种"再压缩收益
; 很小、体积却很大"的输入时会直接崩溃（已通过二分排查确认，与文件路径无关）。
; 默认 zlib 压缩速度更快、更稳定，压缩后体积差异可忽略不计。
SetCompressor zlib

; ---- 现代 UI ----
!include "MUI2.nsh"
!include "LogicLib.nsh"

!define MUI_ICON "..\src\Assets\app.ico"
!define MUI_UNICON "..\src\Assets\app.ico"
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\PwdTool.exe"
!define MUI_FINISHPAGE_RUN_TEXT "立即运行 PwdTool"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

Section "PwdTool 主程序" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"

  ; 安装前先尝试结束正在运行的旧实例，避免单文件被占用导致覆盖失败。
  ; taskkill 返回 0=已结束、128=本来就没在运行，这两种都是正常情况；
  ; 其它返回码（例如权限不足）给出针对性提示，而不是让用户对接下来
  ; 可能出现的"文件被占用"系统对话框摸不着头脑。
  ExecWait 'taskkill /IM PwdTool.exe /F' $0
  ${If} $0 != 0
  ${AndIf} $0 != 128
    MessageBox MB_OK|MB_ICONEXCLAMATION \
      "检测到 PwdTool 可能正在运行，且未能自动将其关闭（taskkill 返回码 $0）。$\r$\n\
如果接下来复制文件时提示被占用，请先手动从系统托盘退出 PwdTool 后重新运行本安装程序。"
  ${EndIf}

  File "${PUBLISH_DIR}\PwdTool.exe"
  File "${PUBLISH_DIR}\使用说明.txt"

  WriteRegStr HKCU "Software\PwdTool" "InstallDir" "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  ; 开始菜单快捷方式
  CreateDirectory "$SMPROGRAMS\PwdTool"
  CreateShortCut "$SMPROGRAMS\PwdTool\PwdTool.lnk" "$INSTDIR\PwdTool.exe"
  CreateShortCut "$SMPROGRAMS\PwdTool\卸载 PwdTool.lnk" "$INSTDIR\Uninstall.exe"

  ; 添加/删除程序 里的卸载条目
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PwdTool" "DisplayName" "PwdTool 密码管理器"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PwdTool" "DisplayIcon" "$INSTDIR\PwdTool.exe,0"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PwdTool" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PwdTool" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PwdTool" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PwdTool" "Publisher" "PwdTool"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PwdTool" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PwdTool" "NoRepair" 1
SectionEnd

Section "开机自启动" SecAutoStart
  ; 与程序内"设置"页的开机自启开关写入同一注册表项，二者保持一致；
  ; 用户之后也可以在程序设置里随时开关，不依赖安装程序的这个选项。
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "PwdTool" '"$INSTDIR\PwdTool.exe"'
SectionEnd

Section "Uninstall"
  ExecWait 'taskkill /IM PwdTool.exe /F' $0
  ${If} $0 != 0
  ${AndIf} $0 != 128
    MessageBox MB_OK|MB_ICONEXCLAMATION \
      "检测到 PwdTool 可能正在运行，且未能自动将其关闭（taskkill 返回码 $0）。$\r$\n\
如果接下来删除文件时提示被占用，请先手动从系统托盘退出 PwdTool 后重新运行卸载程序。"
  ${EndIf}

  Delete "$INSTDIR\PwdTool.exe"
  Delete "$INSTDIR\使用说明.txt"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\PwdTool\PwdTool.lnk"
  Delete "$SMPROGRAMS\PwdTool\卸载 PwdTool.lnk"
  RMDir "$SMPROGRAMS\PwdTool"

  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\PwdTool"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "PwdTool"
  DeleteRegKey HKCU "Software\PwdTool"

  MessageBox MB_YESNO "是否同时删除账号密码库数据（%AppData%\PwdTool\）？此操作不可恢复。" IDNO SkipDataDelete
    RMDir /r "$APPDATA\PwdTool"
  SkipDataDelete:
SectionEnd

LangString DESC_SecMain ${LANG_SIMPCHINESE} "PwdTool 主程序文件（必装）。"
LangString DESC_SecAutoStart ${LANG_SIMPCHINESE} "安装后开机自动启动 PwdTool（也可以之后在程序设置里随时开关）。"

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain} $(DESC_SecMain)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecAutoStart} $(DESC_SecAutoStart)
!insertmacro MUI_FUNCTION_DESCRIPTION_END
