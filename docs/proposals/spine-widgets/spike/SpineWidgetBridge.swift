import Foundation
import WidgetKit
import ActivityKit

// Shared with the widget extension (same source file compiled into both modules).
public struct SpineActivityAttributes: ActivityAttributes {
    public struct ContentState: Codable, Hashable {
        public var json: String
        public init(json: String) { self.json = json }
    }
    public var kind: String
    public init(kind: String) { self.kind = kind }
}

// Objective-C visible facade so .NET can reach WidgetKit/ActivityKit (Swift-only APIs) via objc_msgSend.
@objc(SpineWidgetBridge)
public final class SpineWidgetBridge: NSObject {
    @objc public static func reloadAll() { WidgetCenter.shared.reloadAllTimelines() }
    @objc public static func reload(kind: String) { WidgetCenter.shared.reloadTimelines(ofKind: kind) }

    @objc public static func activitiesEnabled() -> Bool { ActivityAuthorizationInfo().areActivitiesEnabled }

    @objc public static func startActivity(kind: String, json: String) -> String? {
        do {
            let activity = try Activity.request(
                attributes: SpineActivityAttributes(kind: kind),
                content: .init(state: .init(json: json), staleDate: nil),
                pushType: nil)
            return activity.id
        } catch {
            NSLog("[SpineWidgetBridge] start failed: \(error)")
            return nil
        }
    }

    @objc public static func updateActivity(id: String, json: String) {
        Task {
            for activity in Activity<SpineActivityAttributes>.activities where activity.id == id {
                await activity.update(.init(state: .init(json: json), staleDate: nil))
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
}
