import AppIntents
import Foundation

// Compiled into both the widget extension and the bridge framework. The extension needs the type to
// put it on a Button; the app needs it, declared in its own Metadata.appintents, for iOS to run
// perform() in the app's process instead of the extension's. LiveActivityIntent is what asks for
// that, and iOS launches the app in the background when it is not running.

/// The one intent behind every W.Button. It records the tap in the container and posts a Darwin
/// notification. In the app it then waits for .NET to report the tap handled, which keeps the
/// process alive until the widget has been rebuilt with the tap's effect. In the extension — where
/// iOS runs it when the app has no metadata for it — it returns at once, and the app drains the
/// file the next time it is active.
struct SpineWidgetIntent: LiveActivityIntent {
    static var title: LocalizedStringResource = "Spine widget action"
    static var isDiscoverable = false

    @Parameter(title: "Kind") var kind: String
    @Parameter(title: "Action") var actionId: String

    init() {}
    init(kind: String, actionId: String) {
        self.kind = kind
        self.actionId = actionId
    }

    func perform() async throws -> some IntentResult {
        let id = UUID().uuidString
        ActionLog.append(id: id, kind: kind, actionId: actionId)
        ActionLog.notify()
        if ActionLog.isExtension {
            NSLog("[SpineWidgets] tap \"%@\" on %@ recorded by the extension; the app handles it when next active", actionId, kind)
        } else {
            // Apple gives perform() 30 seconds, launch included; leave a margin so a slow handler ends
            // in a reload of whatever is there rather than a system error.
            let started = Date.now
            let handled = await ActionCompletions.wait(id: id, seconds: 25)
            let ms = Int(Date.now.timeIntervalSince(started) * 1000)
            if handled { NSLog("[SpineWidgets] tap \"%@\" on %@ handled by the app in %d ms", actionId, kind, ms) }
            else { NSLog("[SpineWidgets] tap \"%@\" on %@ not answered by the app within %d ms; returning", actionId, kind, ms) }
        }
        return .result()
    }
}

/// The tap log in the App Group container: one JSON line per tap, appended here and consumed by
/// the app's WidgetPlatform.TakeActions.
enum ActionLog {
    /// Written into both Info.plists by the build, so the two processes find the same container.
    static var appGroup: String {
        Bundle.main.object(forInfoDictionaryKey: "SpineWidgetsAppGroup") as? String ?? ""
    }

    static var isExtension: Bool { Bundle.main.bundleURL.pathExtension == "appex" }

    static func append(id: String, kind: String, actionId: String) {
        guard let root = FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: appGroup)?
            .appendingPathComponent("spine-widgets") else { return }
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let line = "{\"id\":\"\(id)\",\"kind\":\"\(kind.escaped)\",\"actionId\":\"\(actionId.escaped)\",\"at\":\(Date.now.timeIntervalSince1970)}\n"
        let url = root.appendingPathComponent("actions.jsonl")
        if let handle = try? FileHandle(forWritingTo: url) {
            handle.seekToEndOfFile(); handle.write(Data(line.utf8)); try? handle.close()
        } else {
            try? line.write(to: url, atomically: true, encoding: .utf8)
        }
    }

    static func notify() {
        let name = CFNotificationName("\(appGroup).spine-widgets.action" as CFString)
        CFNotificationCenterPostNotification(CFNotificationCenterGetDarwinNotifyCenter(), name, nil, nil, true)
    }
}

/// Pairs a perform() waiting in the app with the .NET call that says its tap is handled.
enum ActionCompletions {
    private static let lock = NSLock()
    private static var waiting: [String: CheckedContinuation<Bool, Never>] = [:]
    // Completed before its perform() got to wait — .NET can be that quick — so the wait returns at once.
    private static var completed: Set<String> = []

    /// True when the app answered, false when the wait timed out.
    static func wait(id: String, seconds: Double) async -> Bool {
        await withCheckedContinuation { (continuation: CheckedContinuation<Bool, Never>) in
            lock.lock()
            if completed.remove(id) != nil {
                lock.unlock()
                continuation.resume(returning: true)
                return
            }
            waiting[id] = continuation
            lock.unlock()
            Task {
                try? await Task.sleep(for: .seconds(seconds))
                resume(id, handled: false)
            }
        }
    }

    static func complete(id: String) {
        lock.lock()
        let continuation = waiting.removeValue(forKey: id)
        if continuation == nil {
            if completed.count > 64 { completed.removeAll() }
            completed.insert(id)
        }
        lock.unlock()
        continuation?.resume(returning: true)
    }

    private static func resume(_ id: String, handled: Bool) {
        lock.lock()
        let continuation = waiting.removeValue(forKey: id)
        lock.unlock()
        continuation?.resume(returning: handled)
    }
}

private extension String {
    var escaped: String { replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") }
}
