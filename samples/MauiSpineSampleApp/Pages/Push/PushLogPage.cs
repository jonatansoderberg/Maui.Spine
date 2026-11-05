namespace MauiSpineSampleApp.Pages.Push;

// A singleton, so ShowAsync from a notification finds the one already open instead of stacking another.
[NavigableRegion(Title = "Push log", Lifetime = ServiceLifetime.Singleton)]
public partial class PushLogPage { public PushLogPage() => InitializeComponent(); }
