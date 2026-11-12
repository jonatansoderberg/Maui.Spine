# Windows: a sheet's trailing button hovered as a circle, its leading button as a square

**Branch:** fix/no-initial-focus-visual
**Status:** Completed

## Problem

In a sheet with one leading and one trailing action (AIDash Connect's key sheet: Delete and Cancel), the leading
button's hover was a square and the trailing button's a circle.

## Cause

`HeaderBarView` keeps the trailing actions in a `Border` (`_trailingGroup`), which is the shared glass capsule on
iOS 26 when actions are grouped. Its `StrokeShape` was a capsule on every platform, and a Border clips its content to
its shape. A lone trailing button, as tall as the bar and about as wide, was cut into a circle. In a region's header,
the outer corners of the first and last button were rounded off.

## Change

Without glass header actions (Windows, Android, older iOS) the group's shape is a plain rectangle; with glass it stays
the capsule.

## Verified

- Windows 11, AIDash Connect's key sheet: Delete and Cancel hover as the same 48 × 48 px square (150 %).
