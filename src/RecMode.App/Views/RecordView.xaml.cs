using System.Windows;
using System.Windows.Controls;
using RecMode.App.ViewModels;

namespace RecMode.App.Views;

public partial class RecordView : UserControl
{
    public RecordView() => InitializeComponent();

    // Reports the preview card's actual on-screen pixel size to the view model so the capture preview engine
    // can render at (roughly) the size it'll actually be displayed at instead of always scaling to a fixed
    // 1280×720 regardless of how small the card is laid out — pure waste when the card is a fraction of that.
    // DIPs → physical pixels via this element's own DPI (matches the WriteableBitmap/PresentationSource
    // convention used elsewhere in this app, e.g. WindowPickerOverlay).
    private void OnPreviewSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not RecordViewModel vm)
        {
            return;
        }

        double dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        int pixelWidth = (int)Math.Round(e.NewSize.Width * dpiScale);
        int pixelHeight = (int)Math.Round(e.NewSize.Height * dpiScale);
        vm.SetPreviewSurfaceSize(pixelWidth, pixelHeight);
    }
}
