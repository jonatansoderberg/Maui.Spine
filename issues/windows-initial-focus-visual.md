# Windows: the window opened with a focus rectangle on the header bar's first button

**Branch:** fix/no-initial-focus-visual
**Status:** Completed

## Problem

When a window opens, WinUI focuses its first tab stop and draws it as keyboard focus. In an app with a header bar
(AIDash Connect), that is the first page action: the refresh button opened with a focus rectangle.

## Change

`SpineApplication.Windows.cs`, `SuppressInitialFocusVisual`: the window's first focus, when it isn't from a click,
moves to the window's root view, a ContentControl that draws no focus visual. The root is a tab stop only until it loses
focus. MAUI's window content is a panel (`WindowRootViewContainer`), so the root is the first Control in it. The check
runs on the content's first `GotFocus`, and on the first activation for a focus that came before the hook.

## Verified

- Windows 11, AIDash Connect: the window opens without a rectangle; Tab puts focus on the refresh button with its
  rectangle (UI Automation reports it focused).
