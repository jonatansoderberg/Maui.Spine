using Android;
using Android.App;

// Alarms do not survive a restart, so the boot receiver puts the plan back. The permission is
// normal — granted at install, no dialog — and the package declares it here rather than asking every
// app to remember it.
[assembly: UsesPermission(Manifest.Permission.ReceiveBootCompleted)]
