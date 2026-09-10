using Plugin.Maui.Spine.Svg;

namespace GlassSpike;

public partial class MainPage : ContentPage
{
	int _count;

	public MainPage()
	{
		InitializeComponent();

		var registry = IPlatformApplication.Current!.Services.GetRequiredService<ResourceNameCache>();
		IconButton.ImageSource = SvgBitmapLoader.LoadFromEmbedded(registry.Resolve("Plus.svg") ?? "Plus.svg", 22, 22, Colors.Black, new Thickness(0));
	}

	void OnClicked(object? sender, EventArgs e)
	{
		_count++;
		Counter.Text = $"Tryck: {_count} (senast: {(sender as VisualElement)?.AutomationId})";
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		await Task.Delay(2500);
		Diag.Text = Diagnose();
	}

	string Diagnose()
	{
#if IOS
		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"iOS {UIKit.UIDevice.CurrentDevice.SystemVersion}, scale {UIKit.UIScreen.MainScreen.Scale}");
		foreach (var v in new VisualElement[] { ClearBtn, ClearNoPadBtn, SmallBtn, SmallPlainBtn, IconButton, SvgBtn })
		{
			if (v.Handler?.PlatformView is not UIKit.UIButton b)
				continue;
			var fits = b.SizeThatFits(new CoreGraphics.CGSize(10000, 10000));
			var intrinsic = b.IntrinsicContentSize;
			var ins = b.Configuration?.ContentInsets;
			var insText = ins is null ? "nil" : $"{ins.Value.Top}/{ins.Value.Leading}/{ins.Value.Bottom}/{ins.Value.Trailing}";
			var attrFont = b.Configuration?.AttributedTitle?.Length > 0 ? b.Configuration.AttributedTitle.GetAttribute(UIKit.UIStringAttributeKey.Font, 0, out _)?.Description : "none";
			sb.AppendLine($"{v.AutomationId}: maui={v.Width:0}x{v.Height:0} fits={fits.Width:0.#}x{fits.Height:0.#} intrinsic={intrinsic.Width:0.#}x{intrinsic.Height:0.#} title={b.TitleLabel?.Frame.Width:0.#}x{b.TitleLabel?.Frame.Height:0.#} image={b.ImageView?.Frame.Width:0.#}x{b.ImageView?.Frame.Height:0.#} insets={insText} attrFont={attrFont}");
			var img = b.CurrentImage;
			var imgText = img is null ? "null" : $"{img.Size.Width}x{img.Size.Height}@{img.CurrentScale}x";
			var cfg = b.Configuration is null ? "none" : "set";
			var cfgImg = b.Configuration?.Image is null ? "null" : "set";
			var font = b.TitleLabel?.Font?.PointSize;
			var color = b.TitleLabel?.TextColor?.ToString() ?? "nil";
			var bg = b.BackgroundColor?.ToString() ?? "nil";
			var mid = new CoreGraphics.CGPoint(b.Bounds.X + b.Bounds.Width / 2, b.Bounds.Y + b.Bounds.Height / 2);
			var hit = b.HitTest(mid, null) == b ? "self" : (b.HitTest(mid, null)?.GetType().Name ?? "null");
			var tint = b.TintColor?.ToString() ?? "nil";
			sb.AppendLine($"{v.AutomationId}: cfg={cfg} title='{b.CurrentTitle}' cfgTitle='{b.Configuration?.Title}' cfgImg={cfgImg} img={imgText} font={font} color={color} bounds={b.Bounds.Width:0}x{b.Bounds.Height:0} bg={bg} r={b.Layer.CornerRadius} hit={hit} tint={tint} enabled={b.Enabled}");
		}
		return sb.ToString();
#else
		return string.Empty;
#endif
	}
}
