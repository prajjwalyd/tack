using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Tack.App.Pinboard.Chrome;
using Tack.App.Settings.Sections.Appearance;
using Tack.Core.Appearance;
using Tack.Core.Settings;
using Windows.UI.ViewManagement;
using Frame = Tack.Core.Appearance.Frame;

namespace Tack.App.Settings.Sections;

/// <summary>
/// Appearance: how the board looks, in three cards (the board colour, the
/// frame's finish, the note paper) and a button that puts all three back.
/// Each choice applies to the board at once. The swatches are drawn with the
/// board's own colour maths in this window's theme, and the board colours
/// follow the Windows accent colour.
/// </summary>
public sealed partial class AppearanceSection : UserControl, ISettingsSection
{
    static readonly SwatchChoice[] s_frames = [.. Frame.Presets.Select(p => new SwatchChoice(p.Id, p.Name, p.Name))];
    static readonly SwatchChoice[] s_papers = [.. NotePaper.Presets.Select(p => new SwatchChoice(p.Id, p.Name, p.Name))];

    readonly UISettings _system = new();
    SettingsContext? _context;
    FeltPicker? _picker;
    AppearanceSettings _shown = new();
    Felt.CustomFelt? _lastCustom;
    string _feltsPainted = "";
    bool? _othersPaintedDark;

    public AppearanceSection()
    {
        InitializeComponent();
        FeltCard.Build(FeltChoices.All, square: false);
        FrameCard.Build(s_frames, square: false);
        PaperCard.Build(s_papers, square: true);
        ActualThemeChanged += (_, _) => Paint();
        // Subscribed only while shown, so a closed window is not kept alive by Windows' settings.
        Loaded += (_, _) => _system.ColorValuesChanged += OnColorValuesChanged;
        Unloaded += (_, _) => _system.ColorValuesChanged -= OnColorValuesChanged;
    }

    public string Id => "appearance";

    void ISettingsSection.Start(SettingsContext context)
    {
        _context = context;
        _picker = new FeltPicker(context.Ui);
        _picker.Picked += context.Settings.SetFelt;
        FeltCard.Picked += OnFeltPicked;
        FrameCard.Picked += value =>
        {
            if (Frame.TryParse(value) is { } frame)
            {
                context.Settings.SetFrame(frame);
            }
        };
        PaperCard.Picked += value =>
        {
            if (NotePaper.TryParse(value) is { } paper)
            {
                context.Settings.SetPaper(paper);
            }
        };
        Reset.Click += OnReset;
    }

    void ISettingsSection.Render(TackSettings settings)
    {
        var look = settings.Appearance;
        _shown = look;
        if (look.Felt is Felt.CustomFelt custom)
        {
            _lastCustom = custom;
        }
        FeltCard.Show(FeltChoices.ValueOf(look.Felt), FeltChoices.NameOf(look.Felt));
        FrameCard.Show(look.Frame.Id, look.Frame.Preset.Name);
        PaperCard.Show(look.Paper.Id, look.Paper.Preset.Name);
        Reset.IsEnabled = !AppearanceDefaults.IsDefault(look);
        Paint();
    }

    void OnFeltPicked(string value)
    {
        if (FeltChoices.FeltOf(value) is { } felt)
        {
            _context?.Settings.SetFelt(felt);
        }
        else if (value == FeltChoices.CustomValue && FeltCard.ButtonOf(value) is { } custom)
        {
            _picker?.Open(custom, FeltChoices.PickerStart(_shown.Felt, _lastCustom));
        }
    }

    void OnReset(object sender, RoutedEventArgs e)
    {
        if (_context is null || AppearanceDefaults.IsDefault(_shown))
        {
            return;
        }
        var hadFocus = Reset.FocusState != FocusState.Unfocused;
        _context.Settings.ResetAppearance();
        _context.Announce("Appearance reset");
        // The button is disabled now there is nothing to reset: the focus goes to the board colours rather than away.
        if (hadFocus)
        {
            FeltCard.FocusGroup();
        }
    }

    void OnColorValuesChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(Paint);

    /// <summary>Paints the swatches again when what they show has changed: the theme, the accent colour or a custom colour.</summary>
    void Paint()
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var accent = _system.GetColorValue(UIColorType.Accent).ToRgb();
        var felts = $"{dark} {accent} {_shown.Felt as Felt.CustomFelt}";
        if (felts != _feltsPainted)
        {
            _feltsPainted = felts;
            SwatchPainter.Felts(FeltCard.Faces, _shown.Felt, accent, dark);
        }
        if (_othersPaintedDark != dark)
        {
            _othersPaintedDark = dark;
            SwatchPainter.Frames(FrameCard.Faces, dark);
            SwatchPainter.Papers(PaperCard.Faces, dark);
        }
    }
}
