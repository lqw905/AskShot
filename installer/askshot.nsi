; AskShot NSIS Installer
; 构建前需先运行 scripts/package-windows.ps1 生成 app 目录

!define PRODUCT_NAME "AskShot"
!define PRODUCT_PUBLISHER "AskShot"
!define PRODUCT_WEB_SITE "https://github.com/askshot/AskShot"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\AskShot.Client.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"

; 从命令行传入版本号: makensis /DVERSION=0.2.0 askshot.nsi
!ifndef VERSION
  !define VERSION "0.0.1"
!endif

; 产物目录（相对于 installer/ 的上级目录）
!define BUILD_DIR "..\out\package\windows\app"

SetCompressor /SOLID lzma
SetCompressorDictSize 64

; ── MUI 2 现代界面 ────────────────────────────────────────
!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!insertmacro GetParameters
!insertmacro GetOptions

; ── 安装器元信息 ──────────────────────────────────────────
Name "${PRODUCT_NAME} ${VERSION}"
OutFile "..\out\AskShot-${VERSION}-windows-x64-installer.exe"
InstallDir "$PROGRAMFILES64\${PRODUCT_NAME}"
InstallDirRegKey HKLM "${PRODUCT_DIR_REGKEY}" ""
RequestExecutionLevel admin

; ── 界面设置 ──────────────────────────────────────────────
!define MUI_ABORTWARNING
!define MUI_ICON "..\src\AskShot.Client\AskShot.ico"
!define MUI_UNICON "..\src\AskShot.Client\AskShot.ico"

; 欢迎页
!define MUI_WELCOMEPAGE_TITLE "欢迎安装 ${PRODUCT_NAME}"
!define MUI_WELCOMEPAGE_TEXT "AskShot 是一款智能截图分析工具。截图直接发给视觉语言模型(VLM)，无需 OCR 中转。$\n$\n安装前请关闭正在运行的 AskShot。"

; 完成页
!define MUI_FINISHPAGE_RUN "$INSTDIR\AskShot.Client.exe"
!define MUI_FINISHPAGE_RUN_TEXT "启动 ${PRODUCT_NAME}"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_LINK "访问 AskShot 项目主页"
!define MUI_FINISHPAGE_LINK_LOCATION "${PRODUCT_WEB_SITE}"

; ── 页面序列 ──────────────────────────────────────────────
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

; ── 安装段 ────────────────────────────────────────────────
Section "AskShot (必选)" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"

  ; 主体文件
  File /r "${BUILD_DIR}\*.*"

  ; 确保 service 目录存在
  SetOutPath "$INSTDIR\service"

  ; 快捷方式
  CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
  CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\AskShot.lnk" \
    "$INSTDIR\AskShot.Client.exe" "" "$INSTDIR\AskShot.Client.exe" 0
  CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\卸载 AskShot.lnk" \
    "$INSTDIR\uninst.exe" "" "$INSTDIR\uninst.exe" 0

  ; 桌面快捷方式
  CreateShortCut "$DESKTOP\AskShot.lnk" \
    "$INSTDIR\AskShot.Client.exe" "" "$INSTDIR\AskShot.Client.exe" 0
SectionEnd

; ── 安装后 ────────────────────────────────────────────────
Section -Post
  ; 写入卸载信息
  WriteUninstaller "$INSTDIR\uninst.exe"

  WriteRegStr HKLM "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\AskShot.Client.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" \
    "DisplayName" "$(^Name)"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" \
    "UninstallString" "$INSTDIR\uninst.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" \
    "DisplayIcon" "$INSTDIR\AskShot.Client.exe"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" \
    "DisplayVersion" "${VERSION}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" \
    "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" \
    "URLInfoAbout" "${PRODUCT_WEB_SITE}"
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" \
    "NoModify" 1
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" \
    "NoRepair" 1

  ; 估算大小
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" \
    "EstimatedSize" "$0"
SectionEnd

; ── 卸载段 ────────────────────────────────────────────────
Section Uninstall
  ; 杀掉运行中的进程
  nsExec::ExecToStack 'taskkill /f /im AskShot.Client.exe'
  nsExec::ExecToStack 'taskkill /f /im askshot-service.exe'

  ; 删除程序文件
  RMDir /r "$INSTDIR\service"
  Delete "$INSTDIR\*.*"
  RMDir "$INSTDIR"

  ; 删除快捷方式
  Delete "$SMPROGRAMS\${PRODUCT_NAME}\AskShot.lnk"
  Delete "$SMPROGRAMS\${PRODUCT_NAME}\卸载 AskShot.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT_NAME}"
  Delete "$DESKTOP\AskShot.lnk"

  ; 清理注册表
  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
  DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"

  ;清理用户配置
  MessageBox MB_YESNO|MB_ICONQUESTION \
    "是否同时删除用户数据（分析记录、配置、截图）？$\n$\n路径: $APPDATA\AskShot" \
    IDNO +2
  RMDir /r "$APPDATA\AskShot"
SectionEnd

; ── 安装初始化 ────────────────────────────────────────────
Function .onInit
  ; 禁止重复安装
  System::Call 'kernel32::CreateMutexA(i 0, i 0, t "AskShotSetupMutex") i .r1 ?e'
  Pop $R0
  StrCmp $R0 0 +3
    MessageBox MB_OK|MB_ICONEXCLAMATION "AskShot 安装程序已在运行。"
    Abort

  ; 检查是否已安装
  ReadRegStr $R0 HKLM "${PRODUCT_UNINST_KEY}" "UninstallString"
  StrCmp $R0 "" done

  MessageBox MB_OKCANCEL|MB_ICONQUESTION \
    "检测到已安装的 AskShot（路径: $R0）。$\n$\n继续安装将覆盖现有版本。是否继续？" \
    IDOK done
  Abort
  done:
FunctionEnd

; ── 卸载初始化 ────────────────────────────────────────────
Function un.onInit
  MessageBox MB_YESNO|MB_ICONQUESTION \
    "确定要完全卸载 ${PRODUCT_NAME} 吗？" \
    IDYES +2
  Abort
FunctionEnd
