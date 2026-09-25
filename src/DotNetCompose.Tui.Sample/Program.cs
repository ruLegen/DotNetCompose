using DotNetCompose.Runtime;
using DotNetCompose.Runtime.Snapshots;
using DotNetCompose.Tui;
using static System.Net.Mime.MediaTypeNames;

DotNetCompose.Tui.Sample.TaskEditor.Run();

namespace DotNetCompose.Tui.Sample
{
    internal sealed record TaskItem(int Id, string Title);

    internal static partial class TaskEditor
    {
        private static readonly SnapshotMutableState<string> Input = Composables.CreateMutableState(string.Empty);
        private static readonly SnapshotMutableState<string> Filter = Composables.CreateMutableState(string.Empty);
        private static readonly SnapshotMutableState<int> Selected = Composables.CreateMutableState(0);

        private static readonly SnapshotMutableState<IReadOnlyList<TaskItem>> Items = Composables.CreateMutableState<IReadOnlyList<TaskItem>>(
            [new TaskItem(1, "Stabilize runtime"), new TaskItem(2, "Build TUI")]
        );
        private static readonly TuiTheme GlobalTheme = new TuiTheme(
                Text: new TuiStyle(TuiColor.White),
                Border: new TuiStyle(TuiColor.Cyan),
                Focused: new TuiStyle(TuiColor.White, TuiColor.Green, TuiAttributes.Bold),
                Disabled: new TuiStyle(TuiColor.BrightBlack),
                Selection: new TuiStyle(TuiColor.Yellow, Attributes: TuiAttributes.Bold));

        private static int _nextId = 3;
        internal static void Run() => TuiApplication.Run(Builders.Content);

        [Composable]
        public static void Content()
        {
            IReadOnlyList<TaskItem> visible = Items.Value
                .Where(item => item.Title.Contains(Filter.Value, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            Tui.Theme(GlobalTheme, () =>
            {
                Tui.Column(gap: 1, layout: TuiLayout.Fill, content: () =>
                {
                    if (Items.Value.Count % 2 != 0)
                        Tui.Button("ClearAll", DeleteAll, true);
                    else
                        Tui.Text("DotNetCompose task editor — Tab to move, Enter to activate, Esc to quit");

                    Tui.TextField(Input.Value,
                        onValueChanged: value => Input.Value = value,
                        placeholder: "new task",
                        layout: new TuiLayout(TuiLength.Fill(), TuiLength.Cells(1)));

                    Tui.TextField(Filter.Value,
                        value => Filter.Value = value, "filter",
                        layout: new TuiLayout(TuiLength.Fill(), TuiLength.Cells(1)));

                    Tui.Border(title: "Tasks", layout: TuiLayout.Fill, () =>
                    {
                        Tui.List(visible,
                            key: item => item.Id,
                            selectedIndex: Selected.Value,
                            onSelectionChanged: index => Selected.Value = index,
                            layout: TuiLayout.Fill,
                            itemContent: item =>
                            {
                                Tui.Text(item.Title);
                            });
                    });

                    Tui.Row(gap: 1, content: () =>
                    {
                        Tui.Button("Add", Add, enabled: Input.Value.Length > 0);
                        Tui.Button("Delete", Delete, enabled: visible.Count > 0);
                        Tui.Button("Shuffle " + visible.Count(), Shuffle, enabled: Items.Value.Count > 1);
                    });
                });
            });
        }


        private static void Add()
        {
            if (string.IsNullOrWhiteSpace(Input.Value)) return;
            Items.Value = Items.Value.Append(new TaskItem(_nextId++, Input.Value.Trim())).ToArray();
            Input.Value = string.Empty;
        }

        private static void Delete()
        {
            IReadOnlyList<TaskItem> visible = Items.Value
                .Where(item => item.Title.Contains(Filter.Value, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (visible.Count == 0) return;
            TaskItem selected = visible[Math.Clamp(Selected.Value, 0, visible.Count - 1)];
            Items.Value = Items.Value.Where(item => item.Id != selected.Id).ToArray();
            Selected.Value = Math.Max(0, Math.Min(Selected.Value, Items.Value.Count - 1));
        }
        private static void DeleteAll()
        {
            Items.Value = new List<TaskItem>();
            Selected.Value = Math.Max(0, Math.Min(Selected.Value, Items.Value.Count - 1));
        }

        private static void Shuffle() => Items.Value = Items.Value.Reverse().ToArray();
    }
}
