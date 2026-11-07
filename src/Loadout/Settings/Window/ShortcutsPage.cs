using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Loadout.Settings.Model;
using Loadout.Wheel;
using Loadout.Wheel.Geometry;
using Microsoft.Win32;

namespace Loadout.Settings.Window;

/// <summary>Shortcuts section (SPEC 5): group tabs and the 8 slots laid out like the wheel.</summary>
internal sealed class ShortcutsPage : SectionPage
{
    private const string SlotFormat = "Loadout.Slot";
    private const double CellSize = 84;
    private const double RingRadius = 128;
    private const int IconSize = 48;

    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly SlotCell[] _cells = new SlotCell[WheelMath.SlotCount];
    private readonly TextBlock _centerName = new();
    private readonly TextBlock _status = UiKit.Note(string.Empty);
    private readonly Button _moveLeft;
    private readonly Button _moveRight;
    private readonly Button _rename;
    private readonly Button _delete;
    private int _renaming = -1;
    private SlotCell? _pressed;
    private Point _pressPoint;

    public ShortcutsPage(SettingsContext ctx)
        : base(ctx, "Shortcuts", "Click a slot to set it, or drop apps, folders and shortcuts onto it. Drag one slot onto another to swap them.")
    {
        var tabScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _tabs,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var add = UiKit.MakeButton("+", (_, _) => AddGroup(), null);
        add.Padding = new Thickness(12, 5, 12, 5);
        add.FontSize = 16;
        add.ToolTip = "Add a group";
        add.Margin = new Thickness(8, 0, 0, 0);
        var tabRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(add, Dock.Right);
        tabRow.Children.Add(add);
        tabRow.Children.Add(tabScroll);
        Body.Children.Add(tabRow);

        _moveLeft = Small("◀  Move left", (_, _) => MoveGroup(-1));
        _moveRight = Small("Move right  ▶", (_, _) => MoveGroup(1));
        _rename = Small("Rename", (_, _) => BeginRename(Ctx.ClampedGroup()));
        _delete = Small("Delete", (_, _) => DeleteGroup());
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var b in new[] { _moveLeft, _moveRight, _rename, _delete })
        {
            tools.Children.Add(b);
        }

        Body.Children.Add(tools);
        Body.Children.Add(BuildCircle());

        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.TextAlignment = TextAlignment.Center;
        _status.Margin = new Thickness(0, 6, 0, 0);
        Body.Children.Add(_status);
        Reload();
    }

    public override void Reload()
    {
        Ctx.ClampedGroup();
        _renaming = -1;
        RebuildTabs();
        for (var i = 0; i < _cells.Length; i++)
        {
            UpdateCell(i);
        }
    }

    private static Button Small(string text, RoutedEventHandler click)
    {
        var button = UiKit.MakeButton(text, click, "GhostButton");
        button.FontSize = 12;
        button.Padding = new Thickness(10, 5, 10, 5);
        button.Margin = new Thickness(0, 0, 4, 0);
        return button;
    }

    // ---- Groups ----

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        var groups = Ctx.Current.Groups;
        for (var i = 0; i < groups.Count; i++)
        {
            var index = i;
            if (_renaming == i)
            {
                _tabs.Children.Add(BuildRenameBox(i));
                continue;
            }

            var chip = new RadioButton { Content = groups[i].Name, IsChecked = i == Ctx.SelectedGroup, ToolTip = "Double-click to rename" };
            UiKit.UseStyle(chip, "ChipStyle");
            chip.Click += (_, _) => SelectGroup(index);
            chip.MouseDoubleClick += (_, _) => BeginRename(index);
            _tabs.Children.Add(chip);
        }

        _moveLeft.IsEnabled = Ctx.SelectedGroup > 0;
        _moveRight.IsEnabled = Ctx.SelectedGroup < groups.Count - 1;
        _delete.IsEnabled = groups.Count > 1;
        _delete.ToolTip = groups.Count > 1 ? null : "The last group can't be deleted";
        _centerName.Text = groups[Ctx.ClampedGroup()].Name;
    }

    private TextBox BuildRenameBox(int index)
    {
        var box = new TextBox
        {
            Text = Ctx.Current.Groups[index].Name,
            MinWidth = 120,
            MaxLength = 40,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(10, 6, 10, 6),
        };
        var done = false;
        void Finish(bool save)
        {
            if (done)
            {
                return;
            }

            done = true;
            if (save && !string.IsNullOrWhiteSpace(box.Text) && box.Text.Trim() != Ctx.Current.Groups[index].Name)
            {
                Ctx.Current.Groups[index].Name = box.Text.Trim();
                Ctx.Commit();
            }

            _renaming = -1;
            RebuildTabs();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Finish(true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Finish(false);
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Finish(true);
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        return box;
    }

    private void BeginRename(int index)
    {
        SelectGroup(index);
        _renaming = index;
        RebuildTabs();
    }

    private void SelectGroup(int index)
    {
        if (index == Ctx.SelectedGroup && _renaming < 0)
        {
            return;
        }

        Ctx.SelectedGroup = index;
        _renaming = -1;
        Ctx.HoverSlot = null;
        RebuildTabs();
        for (var i = 0; i < _cells.Length; i++)
        {
            UpdateCell(i);
        }

        Ctx.RefreshPreview();
    }

    private void AddGroup()
    {
        var groups = Ctx.Current.Groups;
        var n = groups.Count + 1;
        while (groups.Any(g => g.Name == "Group " + n))
        {
            n++;
        }

        groups.Add(GroupSettings.CreateEmpty("Group " + n));
        Ctx.Commit();
        SelectGroup(groups.Count - 1);
    }

    private void MoveGroup(int delta)
    {
        var groups = Ctx.Current.Groups;
        var from = Ctx.ClampedGroup();
        var to = from + delta;
        if (to < 0 || to >= groups.Count)
        {
            return;
        }

        (groups[from], groups[to]) = (groups[to], groups[from]);
        var last = Ctx.Current.LastGroupIndex;
        Ctx.Current.LastGroupIndex = last == from ? to : last == to ? from : last;
        Ctx.SelectedGroup = to;
        RebuildTabs();
        Ctx.Commit();
    }

    private void DeleteGroup()
    {
        var groups = Ctx.Current.Groups;
        if (groups.Count <= 1)
        {
            return;
        }

        var index = Ctx.ClampedGroup();
        var ok = SimpleDialog.Confirm(
            Owner,
            "Delete",
            $"Delete \"{groups[index].Name}\" and its shortcuts? This can't be undone.",
            "Delete");
        if (!ok)
        {
            return;
        }

        groups.RemoveAt(index);
        var last = Ctx.Current.LastGroupIndex;
        Ctx.Current.LastGroupIndex = last > index ? last - 1 : Math.Min(last, groups.Count - 1);
        Ctx.SelectedGroup = Math.Min(index, groups.Count - 1);
        Ctx.HoverSlot = null;
        Reload();
        Ctx.Commit();
    }

    // ---- Slot circle ----

    private Canvas BuildCircle()
    {
        var width = (2 * RingRadius) + CellSize;
        var canvas = new Canvas { Width = width, Height = width, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        var center = width / 2;

        _centerName.FontSize = 14;
        _centerName.FontWeight = FontWeights.SemiBold;
        _centerName.Foreground = Palette.DimText;
        _centerName.TextAlignment = TextAlignment.Center;
        _centerName.TextTrimming = TextTrimming.CharacterEllipsis;
        _centerName.Width = 130;
        var hint = new TextBlock
        {
            Text = "Slot 1 is at the top,\nthen clockwise",
            FontSize = 11.5,
            Foreground = Palette.FaintText,
            TextAlignment = TextAlignment.Center,
            Width = 130,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var centerStack = UiKit.Stack(_centerName, hint);
        Canvas.SetLeft(centerStack, center - 65);
        Canvas.SetTop(centerStack, center - 26);
        canvas.Children.Add(centerStack);

        for (var i = 0; i < _cells.Length; i++)
        {
            var cell = new SlotCell(i);
            _cells[i] = cell;
            var (dx, dy) = WheelMath.SlotCenter(i, RingRadius);
            Canvas.SetLeft(cell.Root, center + dx - (CellSize / 2));
            Canvas.SetTop(cell.Root, center + dy - (CellSize / 2));
            WireCell(cell);
            canvas.Children.Add(cell.Root);
        }

        return canvas;
    }

    private void WireCell(SlotCell cell)
    {
        var root = cell.Root;
        root.AllowDrop = true;
        root.MouseEnter += (_, _) =>
        {
            cell.SetHot(true);
            Ctx.HoverSlot = cell.Index;
            Ctx.RefreshPreview();
        };
        root.MouseLeave += (_, _) =>
        {
            cell.SetHot(false);
            if (Ctx.HoverSlot == cell.Index)
            {
                Ctx.HoverSlot = null;
                Ctx.RefreshPreview();
            }
        };
        root.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressed = cell;
            _pressPoint = e.GetPosition(null);
        };
        root.PreviewMouseMove += (_, e) =>
        {
            if (_pressed != cell || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var d = e.GetPosition(null) - _pressPoint;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _pressed = null;
            if (SlotAt(cell.Index) is null)
            {
                return;
            }

            var data = new DataObject(SlotFormat, Ctx.ClampedGroup() + ":" + cell.Index);
            DragDrop.DoDragDrop(root, data, DragDropEffects.Move);
            cell.SetHot(false);
        };
        root.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_pressed == cell)
            {
                _pressed = null;
                ShowMenu(cell);
            }
        };
        root.DragEnter += (_, e) => OnDrag(cell, e);
        root.DragOver += (_, e) => OnDrag(cell, e);
        root.DragLeave += (_, _) => cell.SetHot(false);
        root.Drop += (_, e) => OnDrop(cell, e);
    }

    private SlotSettings? SlotAt(int index)
    {
        var slots = Ctx.Group.Slots;
        return index < slots.Count ? slots[index] : null;
    }

    private void UpdateCell(int index)
    {
        var slot = SlotAt(index);
        _cells[index].Show(slot, slot is null ? null : Ctx.Icons.GetIcon(slot, IconSize), slot is null ? null : SlotLabels.For(slot));
    }

    private void SetSlot(int index, SlotSettings? slot)
    {
        var group = Ctx.Group;
        while (group.Slots.Count < LoadoutSettings.SlotsPerGroup)
        {
            group.Slots.Add(null);
        }

        group.Slots[index] = slot;
        UpdateCell(index);
        Ctx.Commit();
    }

    private void Say(string text, bool bad = false)
    {
        _status.Text = text;
        _status.Foreground = bad ? Palette.Danger : Palette.DimText;
    }

    // ---- Drag and drop ----

    private static void OnDrag(SlotCell cell, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(SlotFormat))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(ShellInterop.ShellIdListFormat))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        cell.SetHot(e.Effects != DragDropEffects.None);
        e.Handled = true;
    }

    private void OnDrop(SlotCell cell, DragEventArgs e)
    {
        cell.SetHot(false);
        e.Handled = true;

        if (e.Data.GetData(SlotFormat) is string payload)
        {
            var parts = payload.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var g) && int.TryParse(parts[1], out var from)
                && g == Ctx.ClampedGroup() && from != cell.Index && from is >= 0 and < LoadoutSettings.SlotsPerGroup)
            {
                SwapSlots(from, cell.Index);
            }

            return;
        }

        SlotSettings? slot = null;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            slot = DropConverter.FromPath(files[0]);
        }

        slot ??= ShellInterop.SlotFromShellIdList(e.Data);

        if (slot is null)
        {
            Say("That can't go on the wheel. Drop a program, shortcut, folder or internet shortcut.", bad: true);
            return;
        }

        SetSlot(cell.Index, slot);
        Say("Added " + SlotLabels.For(slot) + " to slot " + (cell.Index + 1) + ".");
    }

    private void SwapSlots(int a, int b)
    {
        var group = Ctx.Group;
        while (group.Slots.Count < LoadoutSettings.SlotsPerGroup)
        {
            group.Slots.Add(null);
        }

        (group.Slots[a], group.Slots[b]) = (group.Slots[b], group.Slots[a]);
        if (group.LastLaunchedSlot == a)
        {
            group.LastLaunchedSlot = b;
        }
        else if (group.LastLaunchedSlot == b)
        {
            group.LastLaunchedSlot = a;
        }

        UpdateCell(a);
        UpdateCell(b);
        Ctx.Commit();
    }

    // ---- Slot menu ----

    private void ShowMenu(SlotCell cell)
    {
        var index = cell.Index;
        var slot = SlotAt(index);
        var menu = new ContextMenu { PlacementTarget = cell.Root, Placement = PlacementMode.Bottom };

        void Add(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        Add("Choose an app…", () => ChooseApp(index));
        Add("Browse for a program…", () => BrowseProgram(index));
        Add("Choose a folder…", () => ChooseFolder(index));
        Add("Website…", () => ChooseWebsite(index));
        menu.Items.Add(new Separator());
        Add("Custom label…", () => EditLabel(index), slot is not null);
        Add("Custom icon…", () => ChooseIcon(index), slot is not null);
        if (slot?.IconPath is not null)
        {
            Add("Use the automatic icon", () => SetSlot(index, Copy(slot, s => s.IconPath = null)));
        }

        menu.Items.Add(new Separator());
        Add("Clear", () => SetSlot(index, null), slot is not null);
        cell.Root.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static SlotSettings Copy(SlotSettings slot, Action<SlotSettings> change)
    {
        var copy = new SlotSettings
        {
            Type = slot.Type,
            Path = slot.Path,
            Url = slot.Url,
            Arguments = slot.Arguments,
            Label = slot.Label,
            IconPath = slot.IconPath,
        };
        change(copy);
        return copy;
    }

    private void ChooseApp(int index)
    {
        var picker = new AppPickerWindow(Owner, Ctx.Icons);
        if (picker.ShowDialog() == true && picker.Result is { } app)
        {
            SetSlot(index, DropConverter.FromAumid(app.Aumid, app.Name));
        }
    }

    private void BrowseProgram(int index)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a program",
            Filter = "Programs and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(Owner) == true)
        {
            var slot = DropConverter.FromPath(dialog.FileName);
            if (slot is null)
            {
                Say("Choose an .exe program or a .lnk shortcut.", bad: true);
                return;
            }

            SetSlot(index, slot);
        }
    }

    private void ChooseFolder(int index)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder" };
        if (dialog.ShowDialog(Owner) == true && DropConverter.FromPath(dialog.FolderName) is { } slot)
        {
            SetSlot(index, slot);
        }
    }

    private void ChooseWebsite(int index)
    {
        var current = SlotAt(index);
        var text = SimpleDialog.Prompt(
            Owner,
            "Website",
            "Type or paste a web address.",
            current?.Type == SlotType.Website ? current.Url ?? string.Empty : string.Empty,
            input => UrlNormalizer.TryNormalize(input, out _) ? null : "Enter a web address such as example.com or https://example.com.");
        if (text is not null && UrlNormalizer.TryNormalize(text, out var url))
        {
            SetSlot(index, new SlotSettings { Type = SlotType.Website, Url = url });
        }
    }

    private void EditLabel(int index)
    {
        if (SlotAt(index) is not { } slot)
        {
            return;
        }

        var text = SimpleDialog.Prompt(
            Owner,
            "Custom label",
            "The name shown in the middle of the wheel. Leave it empty to use the automatic name.",
            slot.Label ?? SlotLabels.For(slot));
        if (text is not null)
        {
            var label = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            SetSlot(index, Copy(slot, s => s.Label = label));
        }
    }

    private void ChooseIcon(int index)
    {
        if (SlotAt(index) is not { } slot)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose an icon image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico",
        };
        if (dialog.ShowDialog(Owner) == true)
        {
            SetSlot(index, Copy(slot, s => s.IconPath = dialog.FileName));
        }
    }

    /// <summary>One slot in the circle.</summary>
    private sealed class SlotCell
    {
        private readonly Image _image = new() { Width = IconSize, Height = IconSize, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 9, 0, 0), IsHitTestVisible = false };
        private readonly TextBlock _label = new() { FontSize = 11, Foreground = Palette.DimText, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 6), IsHitTestVisible = false };
        private readonly TextBlock _plus = new() { Text = "+", FontSize = 30, FontWeight = FontWeights.Light, Foreground = Palette.FaintText, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        private readonly Rectangle _dash = new() { RadiusX = 12, RadiusY = 12, Stroke = Palette.Line, StrokeThickness = 1.5, StrokeDashArray = [3, 3], IsHitTestVisible = false };
        private bool _filled;

        public SlotCell(int index)
        {
            Index = index;
            var number = new TextBlock { Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 10, Foreground = Palette.FaintText, Margin = new Thickness(8, 5, 0, 0), IsHitTestVisible = false };
            var grid = new Grid();
            grid.Children.Add(_dash);
            grid.Children.Add(_image);
            grid.Children.Add(_label);
            grid.Children.Add(_plus);
            grid.Children.Add(number);
            Root = new Border
            {
                Width = CellSize,
                Height = CellSize,
                CornerRadius = new CornerRadius(12),
                Background = Palette.Make("#01000000"),
                BorderThickness = new Thickness(1.5),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = grid,
            };
        }

        public int Index { get; }

        public Border Root { get; }

        public void Show(SlotSettings? slot, ImageSource? icon, string? label)
        {
            _filled = slot is not null;
            _image.Source = icon;
            _label.Text = label ?? string.Empty;
            _image.Visibility = _label.Visibility = _filled ? Visibility.Visible : Visibility.Collapsed;
            _plus.Visibility = _dash.Visibility = _filled ? Visibility.Collapsed : Visibility.Visible;
            Root.Background = _filled ? Palette.Panel : Palette.Make("#01000000");
        }

        public void SetHot(bool hot) => Root.BorderBrush = hot ? Palette.Accent : Brushes.Transparent;
    }
}
