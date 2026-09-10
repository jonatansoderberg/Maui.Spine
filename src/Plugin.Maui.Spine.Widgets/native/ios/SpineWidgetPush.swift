import Foundation
import WidgetKit

/// iOS 26's widget push. WidgetKit issues the extension a token, and a push to it with
/// `apns-push-type: widgets` reloads every widget that registered this handler — while the app is not
/// running. The app registers the token with its backend, reading it through the bridge, so all this
/// does is tell the app that it changed, in case the app is running; otherwise the app picks it up at
/// its next launch.
@available(iOS 26.0, *)
struct SpineWidgetPushHandler: WidgetPushHandler {
    init() {}

    func pushTokenDidChange(_ pushInfo: WidgetPushInfo, widgets: [WidgetInfo]) {
        NSLog("[SpineWidgets] widget push token changed, covering %d widget(s)", widgets.count)
        let name = CFNotificationName("\(ActionLog.appGroup).spine-widgets.push-token" as CFString)
        CFNotificationCenterPostNotification(CFNotificationCenterGetDarwinNotifyCenter(), name, nil, nil, true)
    }
}
