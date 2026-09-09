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

    /// Starts listening for the push-to-start token and the tokens of activities already running.
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
        for activity in Activity<SpineActivityAttributes>.activities { listen(activity) }
    }

    @objc public static func pushToStartToken() -> String? {
        tokenLock.withLock { startToken }
    }

    @objc public static func pushToken(id: String) -> String? {
        tokenLock.withLock { pushTokens[id] }
    }

    private static func listen(_ activity: Activity<SpineActivityAttributes>) {
        Task {
            for await data in activity.pushTokenUpdates {
                tokenLock.withLock { pushTokens[activity.id] = hex(data) }
            }
        }
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
            if pushTokensEnabled { listen(activity) }
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
