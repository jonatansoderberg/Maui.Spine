import Foundation
import WidgetKit
import ActivityKit

// The one door from .NET into WidgetKit and ActivityKit, which have no Objective-C surface.
// Every member is @objc so the app reaches it with objc_msgSend; no binding project needed.
@objc(SpineWidgetBridge)
public final class SpineWidgetBridge: NSObject {

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
                pushType: nil)
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
