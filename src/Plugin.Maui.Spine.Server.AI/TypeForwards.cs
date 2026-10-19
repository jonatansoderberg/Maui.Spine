using System.Runtime.CompilerServices;
using Plugin.Maui.Spine.Server.AI;

// AICaller moved to Plugin.Maui.Spine.AI.Functions, so a function shared with the app can see who calls it (#632).
[assembly: TypeForwardedTo(typeof(AICaller))]
[assembly: TypeForwardedTo(typeof(AICallerKind))]
