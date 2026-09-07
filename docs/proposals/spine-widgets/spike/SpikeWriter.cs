using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;

namespace SpineWidgetSpike;

static class SpikeWriter
{
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern void Send(IntPtr r, IntPtr sel);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr r, IntPtr sel, IntPtr a, IntPtr b);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern byte SendBool(IntPtr r, IntPtr sel);

    static readonly IntPtr Bridge = Class.GetHandle("SpineWidgetBridge");

    [ModuleInitializer]
    internal static void Init()
    {
        var container = NSFileManager.DefaultManager.GetContainerUrl("group.com.companyname.mauibottomsheetpoc");
        Console.WriteLine($"[spike] app group container: {container?.Path ?? "<null>"}  bridge class: {Bridge}");
        if (container?.Path is { } path)
        {
            var dir = Path.Combine(path, "spine-widgets");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "spine.sample.json"), $$"""
            {"type":"vstack","spacing":6,"children":[
             {"type":"hstack","children":[{"type":"image","systemImage":"figure.run","color":"green"},{"type":"text","text":"Orientera","font":"headline","bold":true},{"type":"spacer"}]},
             {"type":"text","text":"Sthlm Indoor Cup, H21","font":"caption","color":"secondary"},
             {"type":"hstack","spacing":4,"children":[{"type":"text","text":"Start om","font":"headline"},{"type":"timer","until":"{{DateTime.UtcNow.AddMinutes(42):yyyy-MM-dd'T'HH:mm:ss'Z'}}","font":"title","bold":true,"color":"green"}]},
             {"type":"progress","value":0.35,"color":"green"},
             {"type":"text","text":"Skrivet av .NET {{DateTime.Now:HH:mm:ss}}","font":"caption","color":"secondary"}
            ]}
            """);
            Console.WriteLine("[spike] wrote widget json");
        }
        if (Bridge == IntPtr.Zero) return;
        Send(Bridge, Selector.GetHandle("reloadAll"));
        Console.WriteLine($"[spike] reloadAll sent; activitiesEnabled={SendBool(Bridge, Selector.GetHandle("activitiesEnabled"))}");

        _ = Task.Run(async () =>
        {
            await Task.Delay(4000);
            var until = DateTime.UtcNow.AddMinutes(42).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            var json = $$$"""
            {"lockScreen":{"type":"hstack","spacing":10,"children":[
                {"type":"image","systemImage":"figure.run","color":"green"},
                {"type":"vstack","spacing":2,"children":[{"type":"text","text":"Sthlm Indoor Cup · H21","font":"headline","bold":true},{"type":"text","text":"Din start","font":"caption","color":"secondary"}]},
                {"type":"spacer"},
                {"type":"timer","until":"{{{until}}}","font":"title","bold":true,"color":"green"}]},
             "expandedLeading":{"type":"image","systemImage":"figure.run","color":"green"},
             "expandedTrailing":{"type":"timer","until":"{{{until}}}","font":"headline","bold":true,"color":"green"},
             "expandedCenter":{"type":"text","text":"Sthlm Indoor Cup · H21","font":"headline","bold":true},
             "expandedBottom":{"type":"vstack","children":[{"type":"text","text":"Start 11:04 · Bana 6,3 km","font":"caption","color":"secondary"},{"type":"progress","value":0.35,"color":"green"}]},
             "compactLeading":{"type":"image","systemImage":"figure.run","color":"green"},
             "compactTrailing":{"type":"timer","until":"{{{until}}}","font":"caption","bold":true,"color":"green"},
             "minimal":{"type":"image","systemImage":"figure.run","color":"green"}}
            """;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                using var kind = new NSString("spine.race");
                using var payload = new NSString(json);
                var id = Send(Bridge, Selector.GetHandle("startActivityWithKind:json:"), kind.Handle, payload.Handle);
                Console.WriteLine($"[spike] live activity id: {(id == IntPtr.Zero ? "<null>" : NSString.FromHandle(id))}");
            });
        });
    }
}
