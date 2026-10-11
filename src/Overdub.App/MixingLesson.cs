using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class MixingLesson : UserControl
{
    private static readonly string[] Explanations =
    [
        "Levels decide how loud each instrument is. Start with the drums and the bass, because they carry the song. Then fit the guitar, keys and lead around them, so every part can be heard and nothing hides behind another. A good mix is a balance, not everything loud.",
        "Pan places each instrument between the left and the right speaker. Keep the bass and the drums in the middle, where the low end is strongest, and spread the guitar and the keys to either side so they stop fighting for the same spot.",
        "Every instrument lives in a range of pitch. When two of them fight for the same range, both sound muddy. An EQ turns parts of that range down so each one has its own space.",
        "A compressor makes the loud notes quieter so the volume stays even. That makes a bass line sound steady and lets the guitar sit in the mix. Too much squashes the life out of the playing, so use a gentle ratio.",
        "Reverb places a sound in a room and delay adds echoes. A little makes the instruments feel like they belong together, and too much blurs them. Keep the bass dry so the low end stays tight.",
        "Mastering is the last step. It evens out the whole song and sets its loudness so it sounds right next to other songs. Pick a preset for your mixdown. LUFS is how loud a song is to the ear, and streaming services play everything at about -14 LUFS, so making it louder than that gains nothing.",
    ];

    private static readonly string[] Notes =
    [
        "Drums and bass at about 85 percent, the guitar and the lead a little lower, and the keys softer, so the rhythm section leads.",
        "Guitar a little left, keys a little right, the lead just right of centre, and the bass and drums in the middle.",
        "Turn the low end of the guitar down by 4 dB and the lead by 3 dB, so they stop covering the bass.",
        "A 4:1 ratio on the bass for a steady low end, and a gentler 2:1 on the guitar and the lead.",
        "A touch of reverb on the guitar and keys, more reverb and a little delay on the lead, and none on the bass.",
        "For a song you will share online, Streaming, -14 LUFS is a safe choice.",
    ];

    private static readonly string[] ApplyLabels = ["Balance the levels for me", "Spread them out for me", "Make room for me", "Even it out for me", "Add some space for me", ""];

    private static readonly string[] PresetNotes =
    [
        "No processing: your mixdown is exported exactly as it plays.",
        "A gentle lift and light compression for a more finished sound.",
        "Sets the loudness to about -14 LUFS, the level streaming services play at.",
        "Louder and denser, about -9 LUFS. It has more punch but less dynamic range.",
    ];

    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private readonly Dictionary<MixStep, MixPlan> _plans = [];
    private readonly HashSet<MixStep> _done = [];
    private MixStep _step;

    public MixingLesson(LearnContext context)
    {
        _context = context;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        Render();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static string Db(double gain) => gain <= 0.0001 ? "-inf dB" : $"{20 * Math.Log10(gain):+0.0;-0.0;0.0} dB";

    private static string PanText(double pan) => Math.Abs(pan) < 0.005 ? "centre" : pan < 0 ? $"left {-pan * 100:0}" : $"right {pan * 100:0}";

    private MixPlan CurrentPlan()
    {
        if (!_plans.TryGetValue(_step, out var plan) || !plan.Applied)
        {
            plan = _context.Main.PlanMix(_step);
            _plans[_step] = plan;
        }

        return plan;
    }

    private void Go(MixStep step)
    {
        _step = step;
        Render();
    }

    private void Render()
    {
        _page.Children.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _context.Back();
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Mixing basics"));
        _page.Children.Add(Ui.Sub("Six short steps that use your own song. Change something, hear the difference, and keep it or undo it.", 12));
        _page.Children.Add(Pills());
        var plan = CurrentPlan();
        _page.Children.Add(StepCard(plan));
        _page.Children.Add(Navigation());
    }

    private UIElement Pills()
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var step in Enum.GetValues<MixStep>())
        {
            var chosen = step;
            var done = _done.Contains(step) || (step == MixStep.Finish && _context.Main.MasteringChoice > 0);
            var current = step == _step;
            var pill = new Border
            {
                Background = current ? Ui.AccentTint : done ? Frozen(0x1F, 0x33, 0x26) : Ui.Res("Panel"),
                BorderBrush = current ? Ui.AccentBrush : done ? Ui.GoodBrush : Ui.Res("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = $"{(done && !current ? "✓ " : "")}{(int)step + 1} {MixingGuide.StepNames[(int)step]}", FontSize = 12.5, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, Foreground = current ? Frozen(0xF0, 0xDC, 0xB8) : done ? Frozen(0xCF, 0xE6, 0xD4) : Ui.Res("TextDim") },
            };
            pill.OnClick(() => Go(chosen));
            row.Children.Add(pill);
        }

        return row;
    }

    private UIElement StepCard(MixPlan plan)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = $"Step {(int)_step + 1} of 6", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 3) });
        stack.Children.Add(new TextBlock { Text = MixingGuide.StepNames[(int)_step], FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = Explanations[(int)_step], FontSize = 13.5, LineHeight = 20, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }.Glossed(_context));

        if (_step == MixStep.Finish)
        {
            stack.Children.Add(Finish());
        }
        else if (plan.Suggestions.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "Your song has no players yet. Build a song first (Build a song), then come back.", FontSize = 13, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap });
        }
        else
        {
            stack.Children.Add(Visual(plan));
            stack.Children.Add(Note());
            stack.Children.Add(Suggestions(plan));
            stack.Children.Add(Actions(plan));
        }

        var card = Ui.Card(stack, 12);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(20, 16, 20, 16);
        return card;
    }

    private UIElement Visual(MixPlan plan)
    {
        var holder = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        switch (_step)
        {
            case MixStep.Levels:
                holder.Children.Add(Caption("Level of each player (white marker: where I would put it)"));
                holder.Children.Add(new MixBarsControl { Items = plan.Suggestions.Select(s => new MixBarItem(s.Track.Name, s.Current, s.Target, MixBarsControl.RoleColor(s.Role))).ToList() });
                break;
            case MixStep.Pan:
                holder.Children.Add(Caption("Left to right (open circle: now, filled: where I would put it)"));
                holder.Children.Add(new MixBarsControl { IsPan = true, Items = plan.Suggestions.Select(s => new MixBarItem(s.Track.Name, s.Current, s.Target, MixBarsControl.RoleColor(s.Role))).ToList() });
                break;
            case MixStep.Room:
                holder.Children.Add(Caption("Where your instruments sit (low to high pitch)"));
                holder.Children.Add(new FrequencyMapControl { Players = plan.Suggestions.Select(s => (s.Role, s.Track.Name)).ToList() });
                break;
            case MixStep.Compress:
                holder.Children.Add(Caption("What a compressor does: above the threshold, loud notes are turned down (example: the bass setting)"));
                holder.Children.Add(new CompressorCurveControl { Threshold = -18, Ratio = 4, HorizontalAlignment = HorizontalAlignment.Left });
                break;
            default:
                holder.Children.Add(Caption("Reverb for each player (white marker: where I would put it)"));
                holder.Children.Add(new MixBarsControl { Items = plan.Suggestions.Select(s => new MixBarItem(s.Track.Name, s.Current, s.Target, MixBarsControl.RoleColor(s.Role))).ToList() });
                break;
        }

        return holder;
    }

    private static TextBlock Caption(string text) => new() { Text = text, FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 5), TextWrapping = TextWrapping.Wrap };

    private UIElement Note()
    {
        var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
        tip.Inlines.Add(new System.Windows.Documents.Run("What I would change: ") { FontWeight = FontWeights.SemiBold });
        tip.Inlines.AddRange(Gloss.Inlines(Notes[(int)_step], _context));
        return new Border { Background = Frozen(0x1B, 0x2A, 0x22), BorderBrush = Ui.GoodBrush, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 12), Child = tip };
    }

    private UIElement Suggestions(MixPlan plan)
    {
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var suggestion in plan.Suggestions)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = suggestion.Track.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            if (suggestion.CanApply)
            {
                var text = _step switch
                {
                    MixStep.Levels => $"{Db(suggestion.Current)} to {Db(suggestion.Target)}",
                    MixStep.Pan => $"{PanText(suggestion.Current)} to {PanText(suggestion.Target)}",
                    _ => suggestion.Summary,
                };
                stack.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = Frozen(0xCF, 0xCF, 0xD4), TextWrapping = TextWrapping.Wrap });
                if (suggestion.Note is not null)
                {
                    stack.Children.Add(new TextBlock { Text = suggestion.Note, FontSize = 12, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                }
            }
            else
            {
                stack.Children.Add(new TextBlock { Text = suggestion.Note ?? "Nothing to change.", FontSize = 12.5, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap });
            }

            grid.Children.Add(new Border { Background = Ui.Res("Well"), BorderBrush = Ui.Res("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8), Child = stack });
        }

        return grid;
    }

    private UIElement Actions(MixPlan plan)
    {
        var row = new WrapPanel();
        var apply = Ui.Accent(plan.Applied ? "Applied" : ApplyLabels[(int)_step], "Change the real mixer. Undo puts it back.");
        apply.Margin = new Thickness(0, 0, 8, 6);
        apply.OnClick(() =>
        {
            if (plan.Applied)
            {
                return;
            }

            if (!plan.HasChanges)
            {
                _context.Main.ShowNotice("This step is already set the way I would set it.");
                return;
            }

            Apply(plan);
        });
        row.Children.Add(apply);
        var before = Ui.Secondary("Hear before", "Play the song with the mixer as it was", 100);
        before.Click += (_, _) =>
        {
            if (plan.Applied)
            {
                _context.Main.ShowMixState(plan, before: true);
            }

            _context.Main.PlayFromHere();
        };
        row.Children.Add(before);
        var after = Ui.Secondary("Hear after", "Play the song with my suggestion", 100);
        after.Click += (_, _) =>
        {
            if (!plan.Applied)
            {
                if (!plan.HasChanges)
                {
                    _context.Main.ShowNotice("This step is already set the way I would set it.");
                }
                else
                {
                    Apply(plan);
                }
            }
            else
            {
                _context.Main.ShowMixState(plan, before: false);
            }

            _context.Main.PlayFromHere();
        };
        row.Children.Add(after);
        var undo = Ui.Secondary("Undo", "Put the mixer back the way it was", 70);
        undo.Click += (_, _) =>
        {
            if (!plan.Applied)
            {
                return;
            }

            _context.Main.UndoMixPlan(plan);
            _done.Remove(_step);
            Render();
        };
        row.Children.Add(undo);
        return row;
    }

    private void Apply(MixPlan plan)
    {
        _context.Main.ApplyMixPlan(plan);
        _done.Add(_step);
        Render();
    }

    private UIElement Finish()
    {
        var stack = new StackPanel();
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var chosen = _context.Main.MasteringChoice;
        for (var i = 0; i < Mastering.Presets.Count; i++)
        {
            var index = i;
            var selected = i == chosen;
            var chip = new Border
            {
                Background = selected ? Ui.AccentTint : Ui.Res("Panel"),
                BorderBrush = selected ? Ui.AccentBrush : Ui.Res("Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11, 5, 11, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = Mastering.Presets[i].Name, FontSize = 13 },
            };
            chip.OnClick(() =>
            {
                _context.Main.SetMastering(index);
                Render();
            });
            chips.Children.Add(chip);
        }

        stack.Children.Add(chips);
        var description = Frozen(0xCF, 0xE6, 0xD4);
        var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = description };
        tip.Inlines.Add(new System.Windows.Documents.Run($"{Mastering.Presets[chosen].Name}: ") { FontWeight = FontWeights.SemiBold });
        tip.Inlines.Add(new System.Windows.Documents.Run(PresetNotes[Math.Min(chosen, PresetNotes.Length - 1)]));
        stack.Children.Add(new Border { Background = Frozen(0x1B, 0x2A, 0x22), BorderBrush = Ui.GoodBrush, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 10), Child = tip });
        stack.Children.Add(new TextBlock { Text = Notes[(int)MixStep.Finish], FontSize = 13, Foreground = Frozen(0xCF, 0xCF, 0xD4), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(new TextBlock { Text = "Mastering is applied when you export the mixdown (File, Export mixdown). It is not part of playback, so there is nothing to hear here.", FontSize = 12.5, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap });
        return stack;
    }

    private UIElement Navigation()
    {
        var dock = new DockPanel();
        var steps = Enum.GetValues<MixStep>();
        var index = (int)_step;
        if (index < steps.Length - 1)
        {
            var next = Ui.Secondary("Next step ›", "Go to the next step", 110);
            next.Margin = new Thickness(0);
            next.Click += (_, _) => Go(steps[index + 1]);
            DockPanel.SetDock(next, Dock.Right);
            dock.Children.Add(next);
        }

        if (index > 0)
        {
            var previous = Ui.Secondary("‹ Previous step", "Go back one step", 120);
            previous.Margin = new Thickness(0);
            previous.HorizontalAlignment = HorizontalAlignment.Left;
            previous.Click += (_, _) => Go(steps[index - 1]);
            dock.Children.Add(previous);
        }

        return dock;
    }
}
