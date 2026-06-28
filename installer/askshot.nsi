; AskShot NSIS Installer
; Run scripts/package-windows.ps1 before building this

!define PRODUCT_NAME "AskShot"
!define PRODUCT_PUBLISHER "AskShot"
!define PRODUCT_WEB_SITE "https://github.com/lqw905/AskShot"
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\AskShot.Client.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"
!define PRODUCT_UNINST_ROOT_KEY "HKLM"

!ifndef VERSION
  !define VERSION "0.0.1"
!endif

!define BUILD_DIR "..\out\package\windows\app"

SetCompressor /SOLID lzma
SetCompressorDictSize 64

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

!insertmacro GetParameters
!insertmacro GetOptions

Name "${PRODUCT_NAME} ${VERSION}"
OutFile "..\out\AskShot-${VERSION}-windows-x64-installer.exe"
InstallDir "$PROGRAMFILES64\${PRODUCT_NAME}"
InstallDirRegKey HKLM "${PRODUCT_DIR_REGKEY}" ""
RequestExecutionLevel admin

!define MUI_ABORTWARNING
!define MUI_ICON "..\src\AskShot.Client\AskShot.ico"
!define MUI_UNICON "..\src\AskShot.Client\AskShot.ico"

!define MUI_WELCOMEPAGE_TITLE "${PRODUCT_NAME} Setup"
!define MUI_WELCOMEPAGE_TEXT "AskShot is a smart screenshot analysis tool. Screenshots go directly to VLM, no OCR needed.$\n$\nPlease close AskShot before installing."

!define MUI_FINISHPAGE_RUN "$INSTDIR\AskShot.Client.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${PRODUCT_NAME}"
!define MUI_FINISHPAGE_SHOWREADME ""
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!define MUI_FINISHPAGE_LINK "Visit AskShot on GitHub"
!define MUI_FINISHPAGE_LINK_LOCATION "${PRODUCT_WEB_SITE}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "AskShot (required)" SecMain
  SectionIn RO
  SetOutPath "$INSTDIR"

  File /r "${BUILD_DIR}\*.*"

  SetOutPath "$INSTDIR\service"

  CreateDirectory "$SMPROGRAMS\${PRODUCT_NAME}"
  CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\AskShot.lnk" \
    "$INSTDIR\AskShot.Client.exe" "" "$INSTDIR\AskShot.Client.exe" 0
  CreateShortCut "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall AskShot.lnk" \
    "$INSTDIR\uninst.exe" "" "$INSTDIR\uninst.exe" 0

  CreateShortCut "$DESKTOP\AskShot.lnk" \
    "$INSTDIR\AskShot.Client.exe" "" "$INSTDIR\AskShot.Client.exe" 0
SectionEnd

Section -Post
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

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}" \
    "EstimatedSize" "$0"
SectionEnd

Section Uninstall
  nsExec::ExecToStack 'taskkill /f /im AskShot.Client.exe'
  nsExec::ExecToStack 'taskkill /f /im askshot-service.exe'

  RMDir /r "$INSTDIR\service"
  Delete "$INSTDIR\*.*"
  RMDir "$INSTDIR"

  Delete "$SMPROGRAMS\${PRODUCT_NAME}\AskShot.lnk"
  Delete "$SMPROGRAMS\${PRODUCT_NAME}\Uninstall AskShot.lnk"
  RMDir "$SMPROGRAMS\${PRODUCT_NAME}"
  Delete "$DESKTOP\AskShot.lnk"

  DeleteRegKey ${PRODUCT_UNINST_ROOT_KEY} "${PRODUCT_UNINST_KEY}"
  DeleteRegKey HKLM "${PRODUCT_DIR_REGKEY}"

  MessageBox MB_YESNO|MB_ICONQUESTION \
    "Delete user data (analysis history, config, screenshots)?$\n$\nPath: $APPDATA\AskShot" \
    IDNO +2
  RMDir /r "$APPDATA\AskShot"
SectionEnd

Function .onInit
  System::Call 'kernel32::CreateMutexA(i 0, i 0, t "AskShotSetupMutex") i .r1 ?e'
  Pop $R0
  StrCmp $R0 0 +3
    MessageBox MB_OK|MB_ICONEXCLAMATION "AskShot installer is already running."
    Abort

  ReadRegStr $R0 HKLM "${PRODUCT_UNINST_KEY}" "UninstallString"
  StrCmp $R0 "" done

  MessageBox MB_OKCANCEL|MB_ICONQUESTION \
    "AskShot is already installed (path: $R0).$\n$\nContinue to overwrite?" \
    IDOK done
  Abort
  done:
FunctionEnd

Function un.onInit
  MessageBox MB_YESNO|MB_ICONQUESTION \
    "Are you sure you want to uninstall ${PRODUCT_NAME}?" \
    IDYES +2
  Abort
FunctionEnd
