import Foundation
import WidgetKit
import ActivityKit

// The one door from .NET into WidgetKit and ActivityKit, which have no Objective-C surface.
// Every member is @objc so the app reaches it with objc_msgSend; no binding project needed.
@objc(SpineWidgetBridge)
public final class SpineWidgetBridge: NSObject {

    // Push tokens, opt-in from the app. ActivityKit hands them out asynchronously; the latest ones are
    // kept here for the app to poll, since the bridge has no way to call back into .NET.
    private static var pushTokensEnabled = false
    private static var startToken: String?
    private static var pushTokens: [String: String] = [:]
    private static let tokenLock = NSLock()

    // Every activity is observed once, by id: an activity can reach observe() from more than one
    // door — activities at launch, Activity.request, activityUpdates — and one task per door would
    // post the same dismissal twice.
    private static var observed: Set<String> = []

    /// Starts listening for the push-to-start token and the tokens of activities already running.
    /// Call it before `observeActivities`, which is what starts the per-activity token listeners.
    @objc public static func enablePushTokens() {
        guard !pushTokensEnabled else { return }
        pushTokensEnabled = true
        if #available(iOS 17.2, *) {
            Task {
                for await data in Activity<SpineActivityAttributes>.pushToStartTokenUpdates {
                    tokenLock.withLock { startToken = hex(data) }
                }
            }
        }
    }

    /// Watches every activity — those running now and those the system starts later, such as by
    /// push — and posts a Darwin notification when one is dismissed by the user or ended, so the app
    /// can drop it from its list. ActivityKit is the only one who knows: a swipe on the Lock Screen
    /// never reaches the app otherwise.
    @objc public static func observeActivities() {
        for activity in Activity<SpineActivityAttributes>.activities { observe(activity) }
        Task {
            for await activity in Activity<SpineActivityAttributes>.activityUpdates { observe(activity) }
        }
    }

    @objc public static func pushToStartToken() -> String? {
        tokenLock.withLock { startToken }
    }

    @objc public static func pushToken(id: String) -> String? {
        tokenLock.withLock { pushTokens[id] }
    }

    // iOS 26's widget push token. WidgetKit hands it out asynchronously, like ActivityKit, so the latest
    // one is kept for the app to read. Fetched at launch and whenever the extension reports a change.
    private static var widgetToken: String?

    /// Fetches the widget push token and, when it changed, posts `<group>.spine-widgets.push-token` so
    /// the app registers the new one. Returns at once; the fetch is async. Nil before iOS 26, and when
    /// the extension was built without a push handler.
    @objc public static func refreshWidgetPushToken() {
        guard #available(iOS 26.0, *) else { return }
        Task {
            let token = await WidgetCenter.shared.currentPushInfo.map { hex($0.token) }
            let changed = tokenLock.withLock { () -> Bool in
                guard widgetToken != token else { return false }
                widgetToken = token
                return true
            }
            if changed { notifyWidgetPushToken() }
        }
    }

    @objc public static func widgetPushToken() -> String? {
        tokenLock.withLock { widgetToken }
    }

    private static func notifyWidgetPushToken() {
        let name = CFNotificationName("\(ActionLog.appGroup).spine-widgets.push-token" as CFString)
        CFNotificationCenterPostNotification(CFNotificationCenterGetDarwinNotifyCenter(), name, nil, nil, true)
    }

    private static func observe(_ activity: Activity<SpineActivityAttributes>) {
        guard tokenLock.withLock({ observed.insert(activity.id).inserted }) else { return }
        Task {
            for await state in activity.activityStateUpdates where state == .dismissed || state == .ended {
                NSLog("[SpineWidgetBridge] activity %@ (%@) is %@", activity.id, activity.attributes.kind, state == .dismissed ? "dismissed" : "ended")
                tokenLock.withLock { _ = pushTokens.removeValue(forKey: activity.id); observed.remove(activity.id) }
                notifyActivityChanged()
                break
            }
        }
        if pushTokensEnabled {
            Task {
                for await data in activity.pushTokenUpdates {
                    tokenLock.withLock { pushTokens[activity.id] = hex(data) }
                }
            }
        }
    }

    private static func notifyActivityChanged() {
        let name = CFNotificationName("\(ActionLog.appGroup).spine-widgets.activity" as CFString)
        CFNotificationCenterPostNotification(CFNotificationCenterGetDarwinNotifyCenter(), name, nil, nil, true)
    }

    private static func hex(_ data: Data) -> String {
        data.map { String(format: "%02x", $0) }.joined()
    }

    /// The app has handled the button tap with this id, so its perform() can return.
    @objc public static func completeAction(id: String) {
        ActionCompletions.complete(id: id)
    }

    @objc public static func reloadAll() {
        WidgetCenter.shared.reloadAllTimelines()
    }

    @objc public static func reload(kind: String) {
        WidgetCenter.shared.reloadTimelines(ofKind: kind)
    }

    @objc public static func activitiesEnabled() -> Bool {
        ActivityAuthorizationInfo().areActivitiesEnabled
    }

    /// Returns the activity id, or nil when the system refused. `staleAt` is Unix seconds; 0 means none.
    @objc public static func startActivity(kind: String, json: String, staleAt: Double) -> String? {
        do {
            let activity = try Activity.request(
                attributes: SpineActivityAttributes(kind: kind),
                content: .init(state: .init(json: json), staleDate: staleDate(staleAt)),
                pushType: pushTokensEnabled ? .token : nil)
            observe(activity)
            return activity.id
        } catch {
            NSLog("[SpineWidgetBridge] Activity.request failed: \(error)")
            return nil
        }
    }

    @objc public static func updateActivity(id: String, json: String, staleAt: Double) {
        let content = ActivityContent(state: SpineActivityAttributes.ContentState(json: json), staleDate: staleDate(staleAt))
        Task {
            for activity in Activity<SpineActivityAttributes>.activities where activity.id == id {
                await activity.update(content)
            }
        }
    }

    @objc public static func endActivity(id: String) {
        Task {
            for activity in Activity<SpineActivityAttributes>.activities where activity.id == id {
                await activity.end(nil, dismissalPolicy: .immediate)
            }
        }
    }

    /// Every activity this app still has running, as id to kind. Ids survive the app's process,
    /// so this is how the app finds an activity it started before it was killed.
    @objc public static func activeActivities() -> [String: String] {
        Dictionary(Activity<SpineActivityAttributes>.activities.map { ($0.id, $0.attributes.kind) },
                   uniquingKeysWith: { first, _ in first })
    }

    private static func staleDate(_ seconds: Double) -> Date? {
        seconds > 0 ? Date(timeIntervalSince1970: seconds) : nil
    }
}
