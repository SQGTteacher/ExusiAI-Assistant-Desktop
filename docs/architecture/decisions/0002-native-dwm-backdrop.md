# ADR 0002: Use native DWM window materials

## Status

Accepted for phase two.

## Decision

The WPF shell applies Windows 11 system backdrops with `DwmSetWindowAttribute` and lets DWM own the top-level window corner clipping. Users can select Mica, Acrylic, or a solid background. Unsupported Windows versions keep a safe solid rendering.

The implementation follows the same system-backdrop model used by mature WPF shells such as [WPF UI](https://github.com/lepoco/wpfui), while keeping the dependency surface small. It uses Microsoft's documented [`DWM_SYSTEMBACKDROP_TYPE`](https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type) and [`DWM_WINDOW_CORNER_PREFERENCE`](https://learn.microsoft.com/windows/apps/desktop/modernize/ui/apply-rounded-corners) attributes.

## Rationale

- A WPF `Border` with a corner radius does not clip the native HWND and caused content to appear outside the four corners.
- DWM provides the correct Windows 11 outline, shadow, snapping, maximized behavior, and material fallback.
- `AllowsTransparency=True` is intentionally avoided because it changes the WPF rendering path and can degrade resizing and animation performance.
- The backdrop service is isolated in the desktop project so a future Avalonia shell can replace it without changing the theme model or extension runtime.
