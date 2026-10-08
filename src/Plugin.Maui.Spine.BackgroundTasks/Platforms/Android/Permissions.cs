using Android;
using Android.App;

// A persisted job survives a restart only with this permission. It is normal — granted at install, no
// dialog — so the package declares it rather than asking every app to.
[assembly: UsesPermission(Manifest.Permission.ReceiveBootCompleted)]
// The dispatcher leaves a task that needs a network due while the device is offline.
[assembly: UsesPermission(Manifest.Permission.AccessNetworkState)]
