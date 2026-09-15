#if IOS || MACCATALYST
using CoreGraphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;
#endif

namespace TemplateGenerator.App.Handlers;

/// <summary>
/// Gives every text input the same visible edge, and the ones that are not text boxes a
/// glyph that says what they are.
///
/// The style sheet cannot do either. MAUI exposes no stroke on Entry, Editor or Picker, so
/// what you see is the native control: faint on a text field and absent on a text view. And
/// a Picker, DatePicker and TimePicker are all text fields underneath on UIKit, so once
/// every input carries the same frame, a select is indistinguishable from a text box until
/// you tap it.
///
/// It happens here rather than in the generated pages because the converter emits bare
/// controls — 294 of them across the templates converted so far — and appearance is not its
/// job. A handler applies to hand-written pages too, and needs no regeneration.
///
/// Colours come from the app's resource dictionary, so these stay style decisions rather
/// than numbers buried in platform code.
/// </summary>
public static class InputAppearance
{
	private const string BorderLightKey = "InputBorder";
	private const string BorderDarkKey = "InputBorderDark";
	private const string AccessoryLightKey = "InputAccessory";
	private const string AccessoryDarkKey = "InputAccessoryDark";

	/// <summary>The width of the glyph and the gap that keeps it off the border.</summary>
	private const double GlyphWidth = 26;

	/// <summary>The iOS minimum tap target, and the floor under an input with no height of its own.</summary>
	private const double MinimumHeight = 44;

	public static MauiAppBuilder UseHouseInputAppearance(this MauiAppBuilder builder)
	{
#if IOS || MACCATALYST
		EntryHandler.Mapper.AppendToMapping(nameof(InputAppearance), (h, v) =>
		{
			Decorate(h.PlatformView, symbol: null);
			KeepUsable(v);
		});

		EditorHandler.Mapper.AppendToMapping(nameof(InputAppearance), (h, v) =>
		{
			Decorate(h.PlatformView, symbol: null);
			KeepUsable(v);
		});

		// Every control below is a text field underneath, so without a glyph none of them
		// can be told apart from a text box or from each other.
		// These three are text fields underneath on iOS, and native pickers with chrome of
		// their own on Mac Catalyst. Decorate works out which it has been given.
		PickerHandler.Mapper.AppendToMapping(nameof(InputAppearance), (h, v) =>
		{
			Decorate(h.PlatformView, "chevron.down");
			KeepUsable(v);
		});

		DatePickerHandler.Mapper.AppendToMapping(nameof(InputAppearance), (h, v) =>
		{
			Decorate(h.PlatformView, "calendar");
			KeepUsable(v);
		});

		TimePickerHandler.Mapper.AppendToMapping(nameof(InputAppearance), (h, v) =>
		{
			Decorate(h.PlatformView, "clock");
			KeepUsable(v);
		});
#endif
		return builder;
	}

	/// <summary>
	/// A floor under the height, for an input whose page does not set one.
	///
	/// Replacing the native rounded border takes away the padding that came with it, so a
	/// field with no height of its own collapses to the height of its text.
	/// </summary>
	private static void KeepUsable(IView view)
	{
		if (view is VisualElement { HeightRequest: < 0, MinimumHeightRequest: < 0 } element)
			element.MinimumHeightRequest = MinimumHeight;
	}

#if IOS || MACCATALYST
	/// <summary>
	/// Gives one input its edge, and its glyph where it has one to give.
	///
	/// What arrives differs by platform: a Picker is a text field on iOS and a native
	/// control with its own chrome on Mac Catalyst. Matching on what turned up is what makes
	/// one handler work on both, and leaves anything unrecognised exactly as it was.
	/// </summary>
	private static void Decorate(UIView view, string? symbol)
	{
		switch (view)
		{
			case UITextField field:
				// The native rounded border is replaced rather than drawn over, or the two
				// sit one inside the other.
				field.BorderStyle = UITextBorderStyle.None;
				Stroke(field);

				// Without this the text starts hard against the new edge.
				field.LeftView = new UIView(new CGRect(0, 0, 10, 1));
				field.LeftViewMode = UITextFieldViewMode.Always;

				if (symbol is not null) AddGlyph(field, symbol);
				return;

			case UITextView text:
				Stroke(text);
				text.TextContainerInset = new UIEdgeInsets(8, 6, 8, 6);
				return;

			// A native picker: already unmistakably what it is, and drawing a second edge
			// around its own would be one too many.
			default:
				return;
		}
	}

	private static void AddGlyph(UITextField field, string symbol)
	{
		var glyph = UIImage.GetSystemImage(symbol)?
			.ApplyConfiguration(UIImageSymbolConfiguration.Create(12, UIImageSymbolWeight.Semibold));

		if (glyph is null) return;

		var image = new UIImageView(glyph)
		{
			TintColor = Resolve(AccessoryLightKey, AccessoryDarkKey, UIColor.Gray),
			ContentMode = UIViewContentMode.Center,
			Frame = new CGRect(0, 0, 16, 16),
		};

		// Wider than the glyph, so it sits clear of the border rather than against it.
		var container = new UIView(new CGRect(0, 0, GlyphWidth, 16));
		container.AddSubview(image);

		field.RightView = container;
		field.RightViewMode = UITextFieldViewMode.Always;
	}

	private static void Stroke(UIView view)
	{
		view.Layer.BorderWidth = 1;
		view.Layer.CornerRadius = 8;
		view.Layer.BorderColor = Resolve(BorderLightKey, BorderDarkKey, UIColor.LightGray).CGColor;
	}

	private static UIColor Resolve(string lightKey, string darkKey, UIColor fallback)
	{
		var key = Application.Current?.RequestedTheme == AppTheme.Dark ? darkKey : lightKey;

		return Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color colour
			? colour.ToPlatform()
			: fallback;
	}
#endif
}
