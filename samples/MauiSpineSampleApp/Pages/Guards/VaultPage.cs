namespace MauiSpineSampleApp.Pages.Guards;

[NavigableRegion(Title = "Vault"), Confirm("Open the vault?")]
public partial class VaultPage { public VaultPage() => InitializeComponent(); }
