import Foundation
import UserNotifications

/// Adds the picture a Spine push names in `spine.image` before iOS shows the notification.
///
/// iOS runs this in its own process for every push with `mutable-content: 1`, which the server sets
/// only when there is a picture. The rule it keeps is that the notification always arrives: a failed
/// download, a non-image answer or running out of time all deliver the text as it came, and say why in
/// the log — a picture that silently never shows is the harder bug to find.
@objc(SpineNotificationService)
final class SpineNotificationService: UNNotificationServiceExtension {
    private var handler: ((UNNotificationContent) -> Void)?
    private var content: UNMutableNotificationContent?
    private var download: URLSessionDownloadTask?

    override func didReceive(
        _ request: UNNotificationRequest,
        withContentHandler contentHandler: @escaping (UNNotificationContent) -> Void
    ) {
        guard let content = request.content.mutableCopy() as? UNMutableNotificationContent else {
            contentHandler(request.content)
            return
        }

        handler = contentHandler
        self.content = content

        guard let value = content.userInfo["spine.image"] as? String,
              let url = URL(string: value),
              url.scheme == "https" else {
            deliver()
            return
        }

        download = URLSession.shared.downloadTask(with: url) { [weak self] location, response, error in
            guard let self else { return }

            if let location, let attachment = Self.attachment(at: location, response: response, url: url) {
                content.attachments = [attachment]
            } else {
                NSLog("SpineNotificationService: no picture from %@: %@",
                      url.absoluteString, error?.localizedDescription ?? Self.describe(response))
            }

            self.deliver()
        }
        download?.resume()
    }

    override func serviceExtensionTimeWillExpire() {
        download?.cancel()
        NSLog("SpineNotificationService: out of time fetching the picture; delivering the text")
        deliver()
    }

    /// Hands the content back once, whichever of the download and the deadline gets here first.
    private func deliver() {
        guard let handler, let content else { return }
        self.handler = nil
        handler(content)
    }

    private static func attachment(at location: URL, response: URLResponse?, url: URL) -> UNNotificationAttachment? {
        if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) { return nil }

        // UNNotificationAttachment decides the type from the file's extension, and a download lands as
        // .tmp — without renaming it iOS refuses it as an unknown type.
        let target = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString)
            .appendingPathExtension(fileExtension(response: response, url: url))

        do {
            try FileManager.default.moveItem(at: location, to: target)
            return try UNNotificationAttachment(identifier: "spine.image", url: target, options: nil)
        } catch {
            NSLog("SpineNotificationService: could not attach %@: %@", url.absoluteString, error.localizedDescription)
            return nil
        }
    }

    private static func fileExtension(response: URLResponse?, url: URL) -> String {
        switch response?.mimeType {
        case "image/png": return "png"
        case "image/gif": return "gif"
        case "image/heic": return "heic"
        case "image/jpeg", "image/jpg": return "jpg"
        default:
            let named = url.pathExtension.lowercased()
            return named.isEmpty ? "jpg" : named
        }
    }

    private static func describe(_ response: URLResponse?) -> String {
        guard let http = response as? HTTPURLResponse else { return "no response" }
        return "HTTP \(http.statusCode) \(http.mimeType ?? "without a type")"
    }
}
