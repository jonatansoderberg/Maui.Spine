using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Plugin.Maui.Spine.Common;
using static Plugin.Maui.Spine.PushNotifications.WindowsLog;

namespace Plugin.Maui.Spine.PushNotifications;

/// <summary>
/// The toasts the app draws itself — local notifications, and a raw push that carries an alert — and
/// the way back from a tapped one. A toast WNS draws from the server's XML comes back the same way,
/// because both carry the data bag in the toast's <c>launch</c> argument through <see cref="WnsPayload"/>.
/// </summary>
internal static class WindowsNotifications
{
    /// <summary>The prefix of a reply field's id; the button's id follows, so a category can have more than one.</summary>
    private const string ReplyInput = "spine.reply.";

    /// <summary>The toast XML for <paramref name="data"/>, with the buttons of the category it names.</summary>
    internal static string Toast(IReadOnlyDictionary<string, string> data, SpinePushNotificationsOptions options)
    {
        if (data.ContainsKey(WnsPayload.Action))
        {
            Logger?.LogWarning("Spine.PushNotifications: the notification's data has the reserved key '{Key}', which names a tapped button on Windows; it is left out.", WnsPayload.Action);
            data = data.Where(p => p.Key != WnsPayload.Action).ToDictionary(StringComparer.Ordinal);
        }

        var launch = WnsPayload.WriteArguments(data);

        var binding = new XElement("binding", new XAttribute("template", "ToastGeneric"),
            new XElement("text", data.GetValueOrDefault(PushKeys.Title) ?? ""));

        if (data.GetValueOrDefault(PushKeys.Body) is { Length: > 0 } body)
            binding.Add(new XElement("text", body));

        if (Picture(data.GetValueOrDefault(PushKeys.Image)) is { } picture)
            binding.Add(new XElement("image", new XAttribute("placement", "hero"), new XAttribute("src", picture)));

        var toast = new XElement("toast", new XAttribute("launch", launch), new XElement("visual", binding));

        if (data.GetValueOrDefault(PushKeys.Category) is { Length: > 0 } category)
        {
            if (options.Categories.FirstOrDefault(c => c.Id == category) is { } declared)
                toast.Add(Buttons(declared, launch));
            else
                Logger?.LogWarning("Spine.PushNotifications: the notification names category '{Category}', which the app never declared with AddCategory; it has no buttons.", category);
        }

        return toast.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// The buttons, each carrying the toast's arguments plus its own id: a tapped button hands the app
    /// its own arguments and nothing of the toast's (<c>AppNotificationActivatedEventArgs</c> has no way
    /// back to the toast). Windows shows at most five.
    /// </summary>
    private static XElement Buttons(PushCategory category, string launch)
    {
        var inputs = new List<XElement>();
        var buttons = new List<XElement>();

        foreach (var action in category.Actions.Take(5))
        {
            var arguments = $"{launch};{WnsPayload.WriteArguments([new(WnsPayload.Action, action.Id)])}";
            var button = new XElement("action",
                new XAttribute("content", action.Title),
                new XAttribute("arguments", arguments),
                new XAttribute("activationType", action.RunsInBackground ? "background" : "foreground"));

            if (action.Reply is { } placeholder)
            {
                inputs.Add(new XElement("input",
                    new XAttribute("id", ReplyInput + action.Id),
                    new XAttribute("type", "text"),
                    new XAttribute("placeHolderContent", placeholder)));
                button.Add(new XAttribute("hint-inputId", ReplyInput + action.Id));
            }

            buttons.Add(button);
        }

        // The toast schema wants every input before the first action.
        return new XElement("actions", inputs, buttons);
    }

    /// <summary>
    /// A picture as a toast can load it: an https URL as it is, a file on the device as a <c>file:///</c>
    /// URI. A relative path is taken from the app's directory, not the working directory, which is
    /// wherever the app was started from. Anything else leaves the text, and says why.
    /// </summary>
    private static string? Picture(string? image)
    {
        if (image is not { Length: > 0 }) return null;

        if (Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) return uri.AbsoluteUri;

        var path = Path.GetFullPath(image, AppContext.BaseDirectory);
        if (File.Exists(path)) return new Uri(path).AbsoluteUri;

        Logger?.LogWarning("Spine.PushNotifications: no picture: '{Image}' is neither an https URL nor a file on the device. The notification is shown without it.", image);
        return null;
    }

    /// <summary>Shows a toast now, for a raw push that carries an alert.</summary>
    internal static void Show(PushMessage message, SpinePushNotificationsOptions options)
    {
        var notification = new AppNotification(Toast(message.Data, options));
        if (message.CollapseId is { Length: > 0 } collapse) notification.Tag = WnsPayload.Tag(collapse);
        AppNotificationManager.Default.Show(notification);
    }

    /// <summary>What a tapped toast or button carried: the message, the button's id, and a reply's text.</summary>
    internal static (PushMessage Message, string? Action, string? Text) Read(string? argument, IDictionary<string, string>? userInput)
    {
        var data = WnsPayload.ReadArguments(argument);
        var action = data.Remove(WnsPayload.Action, out var id) ? id : null;
        var text = action is not null && userInput?.TryGetValue(ReplyInput + action, out var typed) == true ? typed : null;

        return (PushMessage.From(data), action, text);
    }
}
