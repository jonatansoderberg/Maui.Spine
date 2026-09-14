# Plugin.Maui.Spine.Common

The contracts a Spine app and its server share. A plain `net10.0` library with no MAUI dependency, so it can be referenced from a backend, a domain project or a test project.

```bash
dotnet add package Plugin.Maui.Spine.Common
```

- **The widget tree** — `W`, `WidgetNode`, `WidgetTimeline`, `LiveActivityLayout`, and the `IWidgetProvider`, `IWidgetService` and `ILiveActivityService` interfaces that `Plugin.Maui.Spine.Widgets` implements. A provider can be written and unit-tested without MAUI.
- **Push** — `PushInstallation`, `PushKeys`, `PushTagExpression` and the Live Activity channel names that `Plugin.Maui.Spine.PushNotifications` registers with and `Plugin.Maui.Spine.Server` stores.
- **Serialization** — the JSON contract both sides read and write.

You rarely reference this package directly; `Plugin.Maui.Spine.Widgets`, `Plugin.Maui.Spine.PushNotifications` and `Plugin.Maui.Spine.Server` bring it in.

## Documentation

- [Widgets and Live Activities](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/widgets.md)
- [Push (server)](https://github.com/jonatansoderberg/Maui.Spine/blob/master/docs/wiki/push-notifications-server.md)
- [All packages](https://github.com/jonatansoderberg/Maui.Spine#packages)
