# Findings

- Host: Windows build 26200, x64. .NET Framework 4.8 compiler exists; .NET SDK absent.
- Microsoft LowLevelKeyboardProc documentation: callback runs before async key state update, needs message loop, must return promptly; Windows may silently remove timed-out hooks. Use a dedicated thread and never enumerate windows or read icons in callback.
- Microsoft SetForegroundWindow documentation: foreground acquisition is restricted and can fail; restore then attempt activation and report failure, without claiming a guaranteed bypass.
- DWM cloaked windows are excluded; this does not manage or enumerate all virtual desktops.
- Sources: https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc ; https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow ; https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
- MonitorFromWindow selects largest intersecting monitor, using restored bounds for minimized windows: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-monitorfromwindow
- SendInput menu masking is subject to UIPI and may fail against higher-integrity apps: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
- SetWinEventHook OUTOFCONTEXT notifications need the registering thread to pump messages: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook
- Follow-up clarification: both switchers should show native-style window previews; Win+Tab must be an overview overlay instead of a management window.
- DwmRegisterThumbnail requires a top-level destination owned by this process; DwmUpdateThumbnailProperties publishes live composition and DwmUnregisterThumbnail releases the relationship. No polling capture is needed. Source: https://learn.microsoft.com/en-us/windows/win32/dwm/thumbnail-ovw and https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_thumbnail_properties

## Game compatibility findings
- Microsoft LowLevelKeyboardProc documents that async key state is not updated when the callback runs. A downstream game hook can also filter Win events, so track events received by this hook rather than rely solely on GetAsyncKeyState.
- Microsoft Disabling Shortcut Keys in Games shows fullscreen Win-key filtering via low-level hooks; this is a possibility, not evidence that Arknights uses that implementation.
- TopMost style alone does not explicitly refresh relative Z order on each opening; Present now issues HWND_TOPMOST with NOSIZE/NOMOVE/NOACTIVATE/SHOWWINDOW.
- Test fixture uses a normal borderless monitor-sized topmost WinForms window, not a game renderer. Intermittent focus changes mean no general focus stability claim. Last fixture run passed; actual game still unverified.
