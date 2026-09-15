namespace TemplateGenerator.App.Views.Controls;

/// <summary>
/// One photograph, as a FormWorks Photo field holds one: tap to fill it, tap again to
/// replace it.
///
/// Built on MediaPicker, which is part of MAUI itself, so this needs no package beyond the
/// framework. It is deliberately a house control rather than something the converter knows
/// about: swapping it for another means changing one line of the generator's control
/// vocabulary.
/// </summary>
public partial class PhotoCaptureControl : ContentView
{
	public static readonly BindableProperty PhotoPathProperty =
		BindableProperty.Create(nameof(PhotoPath), typeof(string), typeof(PhotoCaptureControl),
			defaultBindingMode: BindingMode.TwoWay, propertyChanged: OnPhotoPathChanged);

	private static readonly BindablePropertyKey HasPhotoKey =
		BindableProperty.CreateReadOnly(nameof(HasPhoto), typeof(bool), typeof(PhotoCaptureControl), false);

	private static readonly BindablePropertyKey IsEmptyKey =
		BindableProperty.CreateReadOnly(nameof(IsEmpty), typeof(bool), typeof(PhotoCaptureControl), true);

	public static readonly BindableProperty HasPhotoProperty = HasPhotoKey.BindableProperty;

	public static readonly BindableProperty IsEmptyProperty = IsEmptyKey.BindableProperty;

	public PhotoCaptureControl() => InitializeComponent();

	public string? PhotoPath
	{
		get => (string?)GetValue(PhotoPathProperty);
		set => SetValue(PhotoPathProperty, value);
	}

	/// <summary>Drives which of the two layers shows, so the XAML needs no converter of its own.</summary>
	public bool HasPhoto => (bool)GetValue(HasPhotoProperty);

	public bool IsEmpty => (bool)GetValue(IsEmptyProperty);

	private static void OnPhotoPathChanged(BindableObject bindable, object oldValue, object newValue)
	{
		if (bindable is not PhotoCaptureControl control) return;

		var has = !string.IsNullOrWhiteSpace(newValue as string);
		control.SetValue(HasPhotoKey, has);
		control.SetValue(IsEmptyKey, !has);
	}

	private async void OnTapped(object? sender, TappedEventArgs e)
	{
		var page = Application.Current?.Windows.FirstOrDefault()?.Page;
		if (page is null) return;

		try
		{
			var choice = await page.DisplayActionSheetAsync(
				"Photograph", "Cancel", PhotoPath is null ? null : "Remove", "Take photo", "Choose existing");

			if (choice == "Remove")
			{
				PhotoPath = null;
				return;
			}

			if (choice == "Take photo" && !MediaPicker.Default.IsCaptureSupported)
			{
				await page.DisplayAlertAsync("Photograph", "This device has no camera available.", "OK");
				return;
			}

			var result = choice switch
			{
				"Take photo" => await MediaPicker.Default.CapturePhotoAsync(),
				"Choose existing" => (await MediaPicker.Default.PickPhotosAsync()).FirstOrDefault(),
				_ => null,
			};

			// The picker hands back a temporary file, so it is copied into app data or the
			// photograph is gone by the time the report is resumed.
			if (result is not null)
				PhotoPath = await CopyIntoAppDataAsync(result);
		}
		catch (Exception ex)
		{
			await page.DisplayAlertAsync("Photograph", ex.Message, "OK");
		}
	}

	private static async Task<string> CopyIntoAppDataAsync(FileResult result)
	{
		var name = $"{Guid.NewGuid():N}{Path.GetExtension(result.FileName)}";
		var destination = Path.Combine(FileSystem.AppDataDirectory, "photos", name);

		Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

		await using var source = await result.OpenReadAsync();
		await using var target = File.Create(destination);
		await source.CopyToAsync(target);

		return destination;
	}
}
